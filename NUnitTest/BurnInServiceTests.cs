using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace NUnitTest
{
    /// <summary>
    /// BurnInService 的单元测试（仅覆盖纯逻辑：良品判定、关键字拆分、CSV 导出、配置默认值）
    /// 不触碰真实统计数据库，避免污染生产留痕数据
    /// </summary>
    [TestFixture]
    public class BurnInServiceTests
    {
        private BurnInConfig _originalConfig;

        [SetUp]
        public void Setup()
        {
            _originalConfig = BurnInService.Config;
        }

        [TearDown]
        public void TearDown()
        {
            BurnInService.Config = _originalConfig;
        }

        private static BurnInConfig MakeConfig(string goodKeywords = "PASS,OK", string badKeywords = "FAIL,NG")
        {
            return new BurnInConfig
            {
                GoodTextContains = goodKeywords,
                BadTextContains = badKeywords,
                CaptureScreenshot = false,
            };
        }

        [Test]
        public void Evaluate_GoodKeywordHit_IsGood()
        {
            BurnInService.Config = MakeConfig();
            var (isGood, goodText, badText, _, _) = BurnInService.EvaluateAndCapture("result: PASS", false, false, "WO-001");
            Assert.That(isGood, Is.True);
            Assert.That(goodText, Is.EqualTo("result: PASS"));
            Assert.That(badText, Is.Null);
        }

        [Test]
        public void Evaluate_BadKeywordHit_IsBad()
        {
            BurnInService.Config = MakeConfig();
            var (isGood, goodText, badText, _, _) = BurnInService.EvaluateAndCapture("check FAIL here", false, false, "WO-001");
            Assert.That(isGood, Is.False);
            Assert.That(goodText, Is.Null);
            Assert.That(badText, Is.EqualTo("check FAIL here"));
        }

        [Test]
        public void Evaluate_NoKeywordHit_DefaultBad()
        {
            BurnInService.Config = MakeConfig();
            var (isGood, goodText, badText, remark, _) = BurnInService.EvaluateAndCapture("no keyword here", false, false, "WO-001");
            Assert.That(isGood, Is.False);
            Assert.That(goodText, Is.Null);
            Assert.That(badText, Is.Null);
            // 未命中时原始文本写入备注供追溯
            Assert.That(remark, Is.EqualTo("no keyword here"));
        }

        [Test]
        public void Evaluate_GoodImageHit_IsGood()
        {
            BurnInService.Config = MakeConfig();
            var (isGood, _, _, _, _) = BurnInService.EvaluateAndCapture(null, goodImageHit: true, badImageHit: false, "WO-001");
            Assert.That(isGood, Is.True);
        }

        [Test]
        public void Evaluate_BadImageHit_IsBad()
        {
            BurnInService.Config = MakeConfig();
            var (isGood, _, _, _, _) = BurnInService.EvaluateAndCapture(null, goodImageHit: false, badImageHit: true, "WO-001");
            Assert.That(isGood, Is.False);
        }

        [Test]
        public void Evaluate_KeywordCaseInsensitive()
        {
            BurnInService.Config = MakeConfig(goodKeywords: "pass");
            var (isGood, _, _, _, _) = BurnInService.EvaluateAndCapture("RESULT: PASS", false, false, "WO-001");
            Assert.That(isGood, Is.True);
        }

        [Test]
        public void Evaluate_FullWidthCommaSeparatedKeywords()
        {
            // 支持全角逗号分隔
            BurnInService.Config = MakeConfig(goodKeywords: "良品，合格");
            var (isGood, _, _, _, _) = BurnInService.EvaluateAndCapture("判定：合格", false, false, "WO-001");
            Assert.That(isGood, Is.True);
        }

        [Test]
        public void Evaluate_NullOcrText_WithGoodImageHit()
        {
            BurnInService.Config = MakeConfig();
            var (isGood, _, _, _, _) = BurnInService.EvaluateAndCapture(null, true, false, "WO-001");
            Assert.That(isGood, Is.True);
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
                };

                BurnInService.ExportCsv(path, rows);

                Assert.That(File.Exists(path), Is.True);
                string content = File.ReadAllText(path);
                Assert.That(content, Does.Contain("OrderNo"));
                Assert.That(content, Does.Contain("WO-001"));
                Assert.That(content, Does.Contain("张三"));
                Assert.That(content, Does.Contain("Good"));
                Assert.That(content, Does.Contain("Bad"));
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
    }
}
