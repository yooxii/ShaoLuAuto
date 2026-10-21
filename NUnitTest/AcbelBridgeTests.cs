using AcbelMes;
using NUnit.Framework;
using ShaoLu.Services;
using System;

namespace NUnitTest
{
    /// <summary>
    /// Acbel_Chroma_Test.dll 的 x86 桥接（AcbelBridge.exe）测试。
    ///
    /// 说明：
    ///  * 主程序是 x64，无法在进程内加载 32 位原生 DLL，因此所有调用都经由 x86 桥接进程。
    ///  * 按需求，本文件**只测试 ACBEL_DATABASE_OPEN**（通过桥接的 open 命令）。
    ///    ACBEL_INSERT_DATA 会写入 MES 生产库，测试中**不调用**，只覆盖它的安全闸门。
    ///  * 当前环境未接内网、无法连数据库，因此连库测试标记为 [Explicit]（默认不跑），
    ///    需在站点机上手动运行：
    ///        powershell -NoProfile -File tools/test.ps1 -Filter "FullyQualifiedName~AcbelBridgeTests"
    ///    或在 VS 测试资源管理器里显式运行。
    /// </summary>
    [TestFixture]
    public class AcbelBridgeTests
    {
        private static string ResolveBridgeOrNull()
        {
            try { return AcbelBridgeClient.ResolveBridgeExe(); }
            catch (Exception) { return null; }
        }

        /// <summary>一段合法的 27 段上传资料（用于验证"未 OPEN 就 insert"这道闸门先于其它检查生效）。</summary>
        private static string ValidPayload()
        {
            return new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455")
                .Set("MO_NO", "HE47H1G001H18163")
                .Set("STATION", "165")
                .Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735")
                .Set("END_TIME", "140846")
                .Set("TEST_DATE", "20210520")
                .Set("UPD_USER", "21807605")
                .Set("TEST_DATA1", "0.1")
                .Set("TEST_DATA2", "0.2")
                .Set("TEST_DATA3", "0.3")
                .Set("TEST_DATA4", "0.4")
                .Set("OUT_BARCODE", "FSC007A047H1GB2116000136")
                .Set("TOOL_NO", "TOOL")
                .Set("TOOL_NO1", "TOOL1")
                .Set("TOOL_NO2", "TOOL2")
                .Set("TOOL_NO3", "TOOL3")
                .Set("TOOL_NO4", "TOOL4")
                .Set("TOOL_NO5", "TOOL5")
                .Set("TOOL_NO6", "TOOL6")
                .Set("test_data5", "0.5")
                .Set("Laser SN", "GMAU0001")
                .Set("設備編號", "D205")
                .Build();
        }

        // ---------------------------------------------------------------- 不需要数据库
        [Test]
        public void Ping_桥接进程为x86且两个导出可解析()
        {
            string exe = ResolveBridgeOrNull();
            if (exe == null)
                Assert.Ignore("未找到 AcbelBridge.exe：请先构建 AcbelBridge 工程（tools/build-app.ps1），或设置环境变量 ACBEL_BRIDGE_EXE。");

            using (var client = new AcbelBridgeClient(exe, 30))
            {
                string report = client.Ping();
                TestContext.Out.WriteLine("桥接自检：");
                TestContext.Out.WriteLine(report);

                Assert.That(report, Does.Contain("bitness=x86"), "桥接进程必须是 x86，否则无法加载 32 位 DLL");
                Assert.That(report, Does.Contain("export.ACBEL_DATABASE_OPEN=true"));
                Assert.That(report, Does.Contain("export.ACBEL_INSERT_DATA=true"));
            }
        }

        [Test]
        public void InsertData_未成功OPEN前被拒绝且不写库()
        {
            string exe = ResolveBridgeOrNull();
            if (exe == null)
                Assert.Ignore("未找到 AcbelBridge.exe：请先构建 AcbelBridge 工程。");

            using (var client = new AcbelBridgeClient(exe, 30))
            {
                // 每个测试用全新的桥接进程：此时缓存里没有 database，insert 必须在调用 DLL 之前就被拒绝
                AcbelCallResult r = client.InsertData(ValidPayload(), "LASER_TEST", validate: false);

                TestContext.Out.WriteLine("insert 结果：" + r);
                Assert.That(r.TransportOk, Is.True, "桥接进程应正常应答");
                Assert.That(r.IsOk, Is.False, "未 OPEN 前不允许成功");
                Assert.That(r.Error, Does.Contain("尚未成功执行 open"), "桥接必须拒绝并说明原因（未写库）");
            }
        }

        // ---------------------------------------------------------------- 需要站点环境
        [Test]
        [Explicit("会真正尝试连接 MES 数据库：需在站点机（内网 + BDE 别名 + Oracle 客户端）上手动运行；离线环境请勿执行。")]
        public void DatabaseOpen_返回字符串且能判定连通性()
        {
            string exe = ResolveBridgeOrNull();
            if (exe == null)
                Assert.Ignore("未找到 AcbelBridge.exe：请先构建 AcbelBridge 工程。");

            using (var client = new AcbelBridgeClient(exe, 90))
            {
                AcbelCallResult r = client.OpenDatabase();
                TestContext.Out.WriteLine("ACBEL_DATABASE_OPEN -> " + r);

                Assert.That(r.TransportOk, Is.True, "桥接进程应正常应答：" + r.Error);
                Assert.That(r.TimedOut, Is.False, "不应超时");
                Assert.That(r.Value, Is.Not.Null, "DLL 必须返回一个 ANSI 字符串（PChar），不是整数");

                if (AcbelBridgeClient.IsOk(r.Value))
                {
                    // 站点机上连通：返回的就是 BDE 库名（通常 "database"），要原样传给 ACBEL_INSERT_DATA 的第 1 个参数
                    TestContext.Out.WriteLine("数据库连通，库名 = " + r.Value);
                }
                else
                {
                    // 离线/配置缺失：这正是本测试要暴露的信息，不算测试失败
                    TestContext.Out.WriteLine("数据库不连通（预期在离线环境出现）：" + r.Value);
                    Assert.That(
                        AcbelBridgeClient.IsBusinessFail(r.Value) || AcbelBridgeClient.IsRetryNeeded(r.Value),
                        Is.True,
                        "失败时返回值应以 0}（业务卡控）或 2}（内部异常）开头，实际：" + r.Value);
                }
            }
        }
    }
}
