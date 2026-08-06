using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace NUnitTest
{
    /// <summary>
    /// BurnInService 的单元测试（仅覆盖纯逻辑：三态判定、关键字拆分、CSV 导出、配置默认值）
    /// 不触碰真实统计数据库，避免污染生产留痕数据
    /// </summary>
    [TestFixture]
    public class BurnInServiceTests
    {
        private static BurnInConfig MakeConfig(
            string goodKeywords = "PASS,OK",
            string badKeywords = "FAIL,NG",
            string failKeywords = "ERROR,ERR")
        {
            return new BurnInConfig
            {
                GoodTextContains = goodKeywords,
                BadTextContains = badKeywords,
                FailTextContains = failKeywords,
                CaptureScreenshot = false,
            };
        }

        [Test]
        public void Evaluate_GoodKeywordHit_IsGood()
        {
            var (result, goodText, badText, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), "result: PASS", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Good));
            Assert.That(goodText, Is.EqualTo("result: PASS"));
            Assert.That(badText, Is.Null);
        }

        [Test]
        public void Evaluate_BadKeywordHit_IsBad()
        {
            var (result, goodText, badText, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), "check FAIL here", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Bad));
            Assert.That(goodText, Is.Null);
            Assert.That(badText, Is.EqualTo("check FAIL here"));
        }

        [Test]
        public void Evaluate_FailKeywordHit_IsBurnFailed()
        {
            var (result, goodText, badText, remark, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), "burn ERROR occurred", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.BurnFailed));
            Assert.That(goodText, Is.Null);
            Assert.That(badText, Is.Null);
            Assert.That(remark, Is.EqualTo("burn ERROR occurred"));
        }

        [Test]
        public void Evaluate_NoKeywordHit_BurnFailed()
        {
            var (result, goodText, badText, remark, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), "no keyword here", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.BurnFailed));
            Assert.That(goodText, Is.Null);
            Assert.That(badText, Is.Null);
            // 未命中时原始文本写入备注供追溯
            Assert.That(remark, Is.EqualTo("no keyword here"));
        }

        [Test]
        public void Evaluate_BadTakesPriorityOverGood()
        {
            // 同时命中良品与不良关键字：不良品优先（一次性判定唯一结果）
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), "PASS but also FAIL", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Bad));
        }

        [Test]
        public void Evaluate_FailTakesPriorityOverGood()
        {
            // 同时命中良品与失败关键字：烧录失败优先于良品
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), "PASS but also ERROR", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.BurnFailed));
        }

        [Test]
        public void Evaluate_GoodImageHit_IsGood()
        {
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), null, goodImageHit: true, badImageHit: false, failImageHit: false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Good));
        }

        [Test]
        public void Evaluate_BadImageHit_IsBad()
        {
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), null, goodImageHit: true, badImageHit: true, failImageHit: false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Bad));
        }

        [Test]
        public void Evaluate_FailImageHit_IsBurnFailed()
        {
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), null, goodImageHit: true, badImageHit: false, failImageHit: true, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.BurnFailed));
        }

        [Test]
        public void Evaluate_NoImageHit_IsBurnFailed()
        {
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(), null, false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.BurnFailed));
        }

        [Test]
        public void Evaluate_KeywordCaseInsensitive()
        {
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(goodKeywords: "pass"), "RESULT: PASS", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Good));
        }

        [Test]
        public void Evaluate_FullWidthCommaSeparatedKeywords()
        {
            // 支持全角逗号分隔
            var (result, _, _, _, _) = BurnInService.EvaluateAndCapture(
                MakeConfig(goodKeywords: "良品，合格"), "判定：合格", false, false, false, "WO-001");
            Assert.That(result, Is.EqualTo(BurnResult.Good));
        }

        [Test]
        public void ExportCsv_WritesHeaderAndRows()
        {
            string path = Path.Combine(Path.GetTempPath(), $"burnin_test_{Guid.NewGuid():N}.csv");
            try
            {
                var rows = new List<BurnInRecord>
                {
                    new BurnInRecord
                    {
                        Id = 1,
                        OrderNo = "WO-001",
                        Operator = "张三",
                        PartName = "PartA",
                        StepFileName = "demo",
                        BurnStartedAt = new DateTime(2026, 1, 1, 8, 0, 0),
                        BurnFinishedAt = new DateTime(2026, 1, 1, 8, 5, 0),
                        BurnDurationMs = 300000,
                        IsGood = true,
                        GoodText = "PASS",
                    },
                    new BurnInRecord
                    {
                        Id = 2,
                        OrderNo = "WO-001",
                        Operator = "李四",
                        PartName = "PartA",
                        StepFileName = "demo",
                        BurnStartedAt = new DateTime(2026, 1, 1, 9, 0, 0),
                        BurnFinishedAt = new DateTime(2026, 1, 1, 9, 3, 0),
                        BurnDurationMs = 180000,
                        IsGood = false,
                        BadText = "FAIL",
                    },
                    new BurnInRecord
                    {
                        Id = 3,
                        OrderNo = "WO-001",
                        Operator = "王五",
                        PartName = "PartA",
                        StepFileName = "demo",
                        BurnStartedAt = new DateTime(2026, 1, 1, 10, 0, 0),
                        BurnFinishedAt = new DateTime(2026, 1, 1, 10, 2, 0),
                        BurnDurationMs = 120000,
                        IsGood = false,
                        IsBurnFailed = true,
                        Remark = "no keyword",
                    },
                };

                BurnInService.ExportCsv(path, rows);

                Assert.That(File.Exists(path), Is.True);
                string content = File.ReadAllText(path);
                Assert.That(content, Does.Contain("OrderNo"));
                Assert.That(content, Does.Contain("WO-001"));
                Assert.That(content, Does.Contain("张三"));
                Assert.That(content, Does.Contain("Good"));
                Assert.That(content, Does.Contain("Bad"));
                Assert.That(content, Does.Contain("BurnFailed"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void LoadConfig_ReturnsNonNull()
        {
            Assert.That(BurnInService.LoadConfig(), Is.Not.Null);
        }

        [Test]
        public void BurnInConfig_HasRegion_FalseByDefault()
        {
            var config = new BurnInConfig();
            Assert.That(config.HasRegion, Is.False);
            config.RegionX = 0; config.RegionY = 0; config.RegionW = 100; config.RegionH = 100;
            Assert.That(config.HasRegion, Is.True);
        }

        [Test]
        public void BurnInRecord_ResultDisplay_ThreeStates()
        {
            // 三态由 IsGood/IsBurnFailed 组合得出
            Assert.That(new BurnInRecord { IsGood = true }.Result, Is.EqualTo(BurnResult.Good));
            Assert.That(new BurnInRecord { IsGood = false, IsBurnFailed = false }.Result, Is.EqualTo(BurnResult.Bad));
            Assert.That(new BurnInRecord { IsBurnFailed = true }.Result, Is.EqualTo(BurnResult.BurnFailed));
        }
    }
}
