using AcbelMes;
using NUnit.Framework;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// ACBEL_INSERT_DATA 第 2 个参数（上传资料）的字段规范与校验测试。
    /// 纯离线，不接触数据库、不接触 DLL。
    /// 规范来源：Reference\ACBEL_INSERT_DATA-字段与校验.md（27 段，9 段必填）。
    /// </summary>
    [TestFixture]
    public class AcbelInsertDataValidationTests
    {
        /// <summary>一段完全合法的上传资料（27 段）。</summary>
        private static string Valid()
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

        // ------------------------------------------------------------------ 规范本身
        [Test]
        public void 规范_共27段且9段必填()
        {
            Assert.That(AcbelInsertDataSpec.FieldCount, Is.EqualTo(27));
            List<string> required = AcbelInsertDataSpec.RequiredFields.Select(f => f.Name).ToList();
            Assert.That(required, Is.EqualTo(new[]
            {
                "PASS/FAIL", "INTERNAL_SR", "MO_NO", "STATION", "LINE_NAME",
                "START_TIME", "END_TIME", "TEST_DATE", "UPD_USER"
            }));
        }

        [Test]
        public void 规范_段号与名称一致且可按名查字段()
        {
            for (int i = 1; i <= AcbelInsertDataSpec.FieldCount; i++)
            {
                AcbelInsertDataField f = AcbelInsertDataSpec.ByIndex(i);
                Assert.That(f, Is.Not.Null);
                Assert.That(f.Index, Is.EqualTo(i));
                Assert.That(AcbelInsertDataSpec.ByName(f.Name), Is.SameAs(f), "按名称应能查回同一字段：" + f.Name);
            }
            // 名称匹配忽略大小写/空格/下划线
            Assert.That(AcbelInsertDataSpec.ByName("laser_sn").Index, Is.EqualTo(25));
            Assert.That(AcbelInsertDataSpec.ByName("ErrorCode").Index, Is.EqualTo(26));
            Assert.That(AcbelInsertDataSpec.ByName("不存在的字段"), Is.Null);
        }

        // ------------------------------------------------------------------ 正常路径
        [Test]
        public void 合法资料_校验通过()
        {
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(Valid());
            TestContext.Out.WriteLine(r.ToErrorText());
            Assert.That(r.IsValid, Is.True, r.ToErrorText());
            Assert.That(r.Values.Length, Is.EqualTo(27));
            Assert.That(r.Get(1), Is.EqualTo("PASS"));
            Assert.That(r.Get(4), Is.EqualTo("O4769455"));
            Assert.That(r.Get(27), Is.EqualTo("D205"));
        }

        [Test]
        public void 构造器_按名赋值后段序正确()
        {
            string payload = Valid();
            string[] parts = payload.Split(',');
            Assert.That(parts[0], Is.EqualTo("PASS"));
            Assert.That(parts[3], Is.EqualTo("O4769455"), "第 4 段应为 INTERNAL_SR");
            Assert.That(parts[16], Is.EqualTo("TOOL"), "第 17 段应为 TOOL_NO");
            Assert.That(parts[24], Is.EqualTo("GMAU0001"), "第 25 段应为 Laser SN");
            Assert.That(parts[26], Is.EqualTo("D205"), "第 27 段应为 設備編號");
        }

        // ------------------------------------------------------------------ 阻断性错误
        [Test]
        public void 必填为空_报错并指出段号与字段名()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455")
                .Set("MO_NO", "")            // 第 5 段必填
                .Set("STATION", "165")
                .Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735")
                .Set("END_TIME", "140846")
                .Set("TEST_DATE", "20210520")
                .Set("UPD_USER", "21807605")
                .Build();

            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors.Any(e => e.Contains("第 5 段") && e.Contains("MO_NO")), Is.True, r.ToErrorText());
        }

        [Test]
        public void 字段数不足27_报错()
        {
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate("PASS,,,O4769455");
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors.Any(e => e.Contains("字段数不符") && e.Contains("实际 4 段")), Is.True, r.ToErrorText());
        }

        [Test]
        public void 字段数28_默认报错_allowExtraFields时只告警()
        {
            string p28 = Valid() + ",EXTRA";
            AcbelPayloadValidationResult strict = AcbelInsertDataValidator.Validate(p28);
            Assert.That(strict.IsValid, Is.False, "默认必须严格 27 段");
            Assert.That(strict.Errors.Any(e => e.Contains("实际 28 段")), Is.True);

            AcbelPayloadValidationResult relaxed = AcbelInsertDataValidator.Validate(p28, allowExtraFields: true);
            Assert.That(relaxed.IsValid, Is.True);
            Assert.That(relaxed.Warnings.Any(w => w.Contains("多于规范")), Is.True);
        }

        [TestCase("OK")]
        [TestCase("passs")]
        [TestCase("")]
        public void 测试结果非法_报错(string result)
        {
            string payload = new AcbelInsertDataBuilder()
                .Set(1, result)
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735").Set("END_TIME", "140846").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.False);
        }

        [TestCase("14073")]        // 位數不足
        [TestCase("250000")]       // 时超范围
        [TestCase("126100")]       // 分超范围
        [TestCase("AB0735")]       // 非数字
        public void 时间格式非法_报错(string t)
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", t).Set("END_TIME", "140846").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.False, "应判定为非法时间：" + t);
            Assert.That(r.Errors.Any(e => e.Contains("START_TIME")), Is.True);
        }

        [TestCase("1407:35")]      // 合法的带冒号写法
        [TestCase("14:07:35")]
        public void 时间格式合法_带冒号也接受(string t)
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", t).Set("END_TIME", "14:08:46").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.True, r.ToErrorText());
        }

        [TestCase("2021-05-20")]
        [TestCase("2021052")]
        [TestCase("20211332")]     // 无 13 月 32 日
        public void 日期格式非法_报错(string d)
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735").Set("END_TIME", "140846").Set("TEST_DATE", d).Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors.Any(e => e.Contains("TEST_DATE")), Is.True);
        }

        [Test]
        public void 数值字段非数字_报错()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735").Set("END_TIME", "140846").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Set("TEST_DATA1", "一点五")
                .Set("test_data5", "0.5s")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors.Any(e => e.Contains("TEST_DATA1")), Is.True);
            Assert.That(r.Errors.Any(e => e.Contains("test_data5")), Is.True);
        }

        [Test]
        public void 字段前后有空白_告警但放行()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", " O4769455 ").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735").Set("END_TIME", "140846").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Get(4), Is.EqualTo("O4769455"), "应已 trim");
            Assert.That(r.Warnings.Any(w => w.Contains("空白")), Is.True);
        }

        // ------------------------------------------------------------------ 跨字段提示
        [Test]
        public void FAIL但缺失败信息_告警不阻断()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(false).Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165")
                .Set("LINE_NAME", "H18").Set("START_TIME", "140735").Set("END_TIME", "140846")
                .Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.True, "视需求字段为空不算错误");
            Assert.That(r.Warnings.Count(w => w.Contains("FAIL")), Is.GreaterThanOrEqualTo(3), r.ToErrorText());
        }

        [Test]
        public void PASS却填了失败信息_告警()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true, failReason: "NG", failValue: "1.2")
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735").Set("END_TIME", "140846").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Warnings.Any(w => w.Contains("PASS") && w.Contains("FAIL REASON")), Is.True, r.ToErrorText());
        }

        [Test]
        public void 结束时间早于开始时间_告警()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455").Set("MO_NO", "M1").Set("STATION", "165").Set("LINE_NAME", "H18")
                .Set("START_TIME", "140846").Set("END_TIME", "140735").Set("TEST_DATE", "20210520").Set("UPD_USER", "21807605")
                .Build();
            AcbelPayloadValidationResult r = AcbelInsertDataValidator.Validate(payload);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Warnings.Any(w => w.Contains("早於") || w.Contains("早于")), Is.True, r.ToErrorText());
        }

        // ------------------------------------------------------------------ 与桥接客户端集成
        [Test]
        public void 客户端_本地校验不通过时不发起桥接调用()
        {
            AcbelBridgeClient client;
            try { client = new AcbelBridgeClient(); }
            catch (System.IO.FileNotFoundException) { Assert.Ignore("未找到 AcbelBridge.exe：请先构建 AcbelBridge 工程。"); return; }

            // 字段数不足：应直接返回错误，ValidationErrors 有内容，且不抛异常、不发起桥接调用
            using (client)
            {
                AcbelCallResult r = client.InsertData("PASS,,,ONLY4", "LASER_TEST");
                Assert.That(r.TransportOk, Is.False);
                Assert.That(r.ValidationErrors.Count, Is.GreaterThan(0));
                Assert.That(r.Error, Does.Contain("字段数不符"));
            }
        }

        [Test]
        public void 客户端_静态校验入口可用()
        {
            Assert.That(AcbelBridgeClient.ValidateInsertData(Valid()).IsValid, Is.True);
            Assert.That(AcbelBridgeClient.ValidateInsertData("").IsValid, Is.False);
        }
    }
}
