// -----------------------------------------------------------------------------
//  AcbelInsertDataSpec —— ACBEL_INSERT_DATA 第 2 个参数（上传资料）的字段规范与校验
//
//  本文件被两个工程共享编译（链接同一份源码，避免规范写两遍）：
//      * AcbelBridge\AcbelBridge.csproj  （x86 桥接，调用 DLL 前再校验一次）
//      * ShaoLu\ShaoLu.csproj            （x64 主程序，构造/提交前校验）
//
//  规范来源：用户提供的字段清单截图（27 个字段，其中 9 个必填），
//  说明见 Reference\ACBEL_INSERT_DATA-字段与校验.md。
//
//  载荷格式：一个逗号分隔的定长字符串，按下面的顺序拼 27 段；空字段也要保留逗号占位。
//  示例（厂商说明书里的样例，注意它是 28 段、含 8 个治具，属于旧版口径）：
//      PASS,,,O4769455,HE47H1G001H18163,165,H18,140735,140846,20210520,21807605,0.1,0.2,0.3,0.4,...
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AcbelMes
{
    /// <summary>字段的值类型（只用于基本格式校验）。</summary>
    public enum AcbelFieldKind
    {
        /// <summary>普通文本，不做格式校验。</summary>
        Text,
        /// <summary>PASS / FAIL。</summary>
        PassFail,
        /// <summary>HHMMSS 或 HH:MM:SS。</summary>
        Time,
        /// <summary>YYYYMMDD。</summary>
        Date,
        /// <summary>十进制数字（可带小数、负号）。</summary>
        Decimal
    }

    /// <summary>上传资料中的一个字段。</summary>
    public sealed class AcbelInsertDataField
    {
        public AcbelInsertDataField(int index, string name, string description, bool required, AcbelFieldKind kind)
        {
            Index = index; Name = name; Description = description; Required = required; Kind = kind;
        }

        /// <summary>1 起的段号。</summary>
        public int Index { get; private set; }
        /// <summary>字段名（MES 栏位名）。</summary>
        public string Name { get; private set; }
        /// <summary>中文说明。</summary>
        public string Description { get; private set; }
        /// <summary>是否必填。</summary>
        public bool Required { get; private set; }
        public AcbelFieldKind Kind { get; private set; }

        public override string ToString()
        {
            return string.Format("{0}){1}:{2}{3}", Index, Name, Description, Required ? "(必填)" : "(视需求)");
        }
    }

    /// <summary>ACBEL_INSERT_DATA 第 2 个参数的字段规范。</summary>
    public static class AcbelInsertDataSpec
    {
        /// <summary>规范字段（按段号升序）。</summary>
        public static readonly IReadOnlyList<AcbelInsertDataField> Fields = new[]
        {
            new AcbelInsertDataField( 1, "PASS/FAIL",   "測試結果",      true,  AcbelFieldKind.PassFail),
            new AcbelInsertDataField( 2, "FAIL REASON","失敗原因",      false, AcbelFieldKind.Text),
            new AcbelInsertDataField( 3, "FAIL_VALUE", "失敗值",        false, AcbelFieldKind.Text),
            new AcbelInsertDataField( 4, "INTERNAL_SR","單體條碼",      true,  AcbelFieldKind.Text),
            new AcbelInsertDataField( 5, "MO_NO",      "工令",          true,  AcbelFieldKind.Text),
            new AcbelInsertDataField( 6, "STATION",    "站別",          true,  AcbelFieldKind.Text),
            new AcbelInsertDataField( 7, "LINE_NAME",  "線別",          true,  AcbelFieldKind.Text),
            new AcbelInsertDataField( 8, "START_TIME", "測試開始時間",  true,  AcbelFieldKind.Time),
            new AcbelInsertDataField( 9, "END_TIME",   "測試結束時間",  true,  AcbelFieldKind.Time),
            new AcbelInsertDataField(10, "TEST_DATE",  "測試日期",      true,  AcbelFieldKind.Date),
            new AcbelInsertDataField(11, "UPD_USER",   "測試員工號",    true,  AcbelFieldKind.Text),
            new AcbelInsertDataField(12, "TEST_DATA1", "深度",          false, AcbelFieldKind.Decimal),
            new AcbelInsertDataField(13, "TEST_DATA2", "能量",          false, AcbelFieldKind.Decimal),
            new AcbelInsertDataField(14, "TEST_DATA3", "超聲時間",      false, AcbelFieldKind.Decimal),
            new AcbelInsertDataField(15, "TEST_DATA4", "壓力",          false, AcbelFieldKind.Decimal),
            new AcbelInsertDataField(16, "OUT_BARCODE","外控條碼",      false, AcbelFieldKind.Text),
            new AcbelInsertDataField(17, "TOOL_NO",    "治具1",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(18, "TOOL_NO1",   "治具2",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(19, "TOOL_NO2",   "治具3",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(20, "TOOL_NO3",   "治具4",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(21, "TOOL_NO4",   "治具5",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(22, "TOOL_NO5",   "治具6",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(23, "TOOL_NO6",   "治具7",         false, AcbelFieldKind.Text),
            new AcbelInsertDataField(24, "test_data5", "結合時間",      false, AcbelFieldKind.Decimal),
            new AcbelInsertDataField(25, "Laser SN",   "實際鐳雕條碼",  false, AcbelFieldKind.Text),
            new AcbelInsertDataField(26, "Error Code", "CCD 不良代碼",  false, AcbelFieldKind.Text),
            new AcbelInsertDataField(27, "設備編號",    "設備編號",      false, AcbelFieldKind.Text),
        };

        public static int FieldCount { get { return Fields.Count; } }

        public static IEnumerable<AcbelInsertDataField> RequiredFields { get { return Fields.Where(f => f.Required); } }

        /// <summary>按 1 起段号取字段；越界返回 null。</summary>
        public static AcbelInsertDataField ByIndex(int index)
        {
            return index >= 1 && index <= Fields.Count ? Fields[index - 1] : null;
        }

        /// <summary>按名称取字段（忽略大小写、空格、下划线）；找不到返回 null。</summary>
        public static AcbelInsertDataField ByName(string name)
        {
            string key = Normalize(name);
            if (key.Length == 0) return null;
            return Fields.FirstOrDefault(f => Normalize(f.Name) == key);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == ' ' || c == '_' || c == '-' || c == ':' || c == '\t') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }

    /// <summary>校验结果。</summary>
    public sealed class AcbelPayloadValidationResult
    {
        public AcbelPayloadValidationResult()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
            Values = new string[0];
        }

        /// <summary>阻断性错误：有任意一条就不应调用 ACBEL_INSERT_DATA。</summary>
        public List<string> Errors { get; private set; }

        /// <summary>提示性问题：不阻断，但值得确认。</summary>
        public List<string> Warnings { get; private set; }

        /// <summary>拆分并 trim 后的字段值（长度可能少于规范字段数）。</summary>
        public string[] Values { get; set; }

        public bool IsValid { get { return Errors.Count == 0; } }

        /// <summary>取某个字段值（1 起段号）；越界或未提供返回空串。</summary>
        public string Get(int index)
        {
            if (index < 1 || Values == null || index > Values.Length) return string.Empty;
            return Values[index - 1] ?? string.Empty;
        }

        public string ToErrorText()
        {
            var sb = new StringBuilder();
            if (Errors.Count > 0)
            {
                sb.Append("数据校验未通过（").Append(Errors.Count).Append(" 项错误）：");
                for (int i = 0; i < Errors.Count; i++) sb.Append("\n  ").Append(i + 1).Append(". ").Append(Errors[i]);
            }
            if (Warnings.Count > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("提示（").Append(Warnings.Count).Append(" 项）：");
                for (int i = 0; i < Warnings.Count; i++) sb.Append("\n  ").Append(i + 1).Append(". ").Append(Warnings[i]);
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return IsValid
                ? (Warnings.Count == 0 ? "校验通过" : "校验通过（有 " + Warnings.Count + " 项提示）")
                : "校验失败：" + Errors.Count + " 项错误";
        }
    }

    /// <summary>ACBEL_INSERT_DATA 第 2 个参数的校验器。</summary>
    public static class AcbelInsertDataValidator
    {
        /// <summary>
        /// 校验上传资料字符串。
        /// </summary>
        /// <param name="payload">逗号分隔的定长字段串。</param>
        /// <param name="allowExtraFields">
        /// true：字段数多于规范时只告警（旧版说明书样例是 28 段）。false（默认）：必须恰好等于规范段数。
        /// </param>
        public static AcbelPayloadValidationResult Validate(string payload, bool allowExtraFields = false)
        {
            var r = new AcbelPayloadValidationResult();
            if (payload == null)
            {
                r.Errors.Add("上传资料为 null");
                return r;
            }

            string[] raw = payload.Split(',');
            var values = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++) values[i] = raw[i].Trim();
            r.Values = values;

            // ---- 字段数 ----
            if (raw.Length != AcbelInsertDataSpec.FieldCount)
            {
                if (raw.Length > AcbelInsertDataSpec.FieldCount && allowExtraFields)
                {
                    r.Warnings.Add(string.Format(
                        "字段数多于规范：实际 {0} 段，规范 {1} 段；已按 allowExtraFields 放行，多出的字段可能被 DLL 忽略",
                        raw.Length, AcbelInsertDataSpec.FieldCount));
                }
                else
                {
                    r.Errors.Add(string.Format(
                        "字段数不符：实际 {0} 段，规范 {1} 段（{2}…{3}）。请按 Reference\\ACBEL_INSERT_DATA-字段与校验.md 的顺序补齐/删除（空字段也要留逗号占位）",
                        raw.Length, AcbelInsertDataSpec.FieldCount,
                        AcbelInsertDataSpec.Fields[0].Name,
                        AcbelInsertDataSpec.Fields[AcbelInsertDataSpec.FieldCount - 1].Name));
                }
            }

            // ---- 逐段校验 ----
            int common = Math.Min(values.Length, AcbelInsertDataSpec.FieldCount);
            for (int i = 0; i < common; i++)
            {
                AcbelInsertDataField f = AcbelInsertDataSpec.Fields[i];
                string v = values[i];

                if (v.Length == 0)
                {
                    if (f.Required)
                        r.Errors.Add(string.Format("第 {0} 段 {1}（{2}）为必填，不能为空", f.Index, f.Name, f.Description));
                    continue;
                }

                if (!string.Equals(v, raw[i], StringComparison.Ordinal))
                    r.Warnings.Add(string.Format("第 {0} 段 {1}（{2}）前后有空白，已按 trim 处理：\"{3}\"", f.Index, f.Name, f.Description, raw[i]));

                switch (f.Kind)
                {
                    case AcbelFieldKind.PassFail:
                        if (!string.Equals(v, "PASS", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(v, "FAIL", StringComparison.OrdinalIgnoreCase))
                            r.Errors.Add(string.Format("第 {0} 段 {1}（{2}）只能是 PASS 或 FAIL，实际 \"{3}\"", f.Index, f.Name, f.Description, v));
                        break;

                    case AcbelFieldKind.Time:
                        if (!IsValidTime(v, out string timeErr))
                            r.Errors.Add(string.Format("第 {0} 段 {1}（{2}）格式应为 HHMMSS 或 HH:MM:SS：{3}", f.Index, f.Name, f.Description, timeErr));
                        break;

                    case AcbelFieldKind.Date:
                        if (!IsValidDate(v, out string dateErr))
                            r.Errors.Add(string.Format("第 {0} 段 {1}（{2}）格式应为 YYYYMMDD：{3}", f.Index, f.Name, f.Description, dateErr));
                        break;

                    case AcbelFieldKind.Decimal:
                        if (!decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            r.Errors.Add(string.Format("第 {0} 段 {1}（{2}）应为数字，实际 \"{3}\"", f.Index, f.Name, f.Description, v));
                        break;
                }
            }

            // ---- 跨字段一致性 ----
            string result = r.Get(1);
            bool isFail = string.Equals(result, "FAIL", StringComparison.OrdinalIgnoreCase);
            bool isPass = string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase);

            if (isFail)
            {
                if (r.Get(2).Length == 0) r.Warnings.Add("測試結果為 FAIL，但第 2 段 FAIL REASON（失敗原因）为空");
                if (r.Get(3).Length == 0) r.Warnings.Add("測試結果為 FAIL，但第 3 段 FAIL_VALUE（失敗值）为空");
                if (r.Get(26).Length == 0) r.Warnings.Add("測試結果為 FAIL，但第 26 段 Error Code（CCD 不良代碼）为空");
            }
            else if (isPass)
            {
                if (r.Get(2).Length > 0 || r.Get(3).Length > 0)
                    r.Warnings.Add("測試結果為 PASS，但第 2/3 段（FAIL REASON / FAIL_VALUE）有值，请确认是否填错");
            }

            if (IsValidTime(r.Get(8), out _) && IsValidTime(r.Get(9), out _))
            {
                int s = ToSeconds(r.Get(8)), e = ToSeconds(r.Get(9));
                if (e < s) r.Warnings.Add(string.Format("測試結束時間（{0}）早於測試開始時間（{1}），若为跨零点请忽略", r.Get(9), r.Get(8)));
            }

            return r;
        }

        private static bool IsValidTime(string v, out string error)
        {
            error = null;
            string digits = v.Replace(":", string.Empty);
            if (digits.Length != 6 || !digits.All(char.IsDigit))
            {
                error = "不是 6 位数字";
                return false;
            }
            int hh = int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture);
            int mm = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
            int ss = int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture);
            if (hh > 23 || mm > 59 || ss > 59)
            {
                error = string.Format("时/分/秒超范围（{0:D2}:{1:D2}:{2:D2}）", hh, mm, ss);
                return false;
            }
            return true;
        }

        private static int ToSeconds(string v)
        {
            string digits = v.Replace(":", string.Empty);
            return int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture) * 3600
                 + int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture) * 60
                 + int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture);
        }

        private static bool IsValidDate(string v, out string error)
        {
            error = null;
            if (v.Length != 8 || !v.All(char.IsDigit))
            {
                error = "不是 8 位数字";
                return false;
            }
            if (!DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                error = "不是合法日期";
                return false;
            }
            return true;
        }
    }

    /// <summary>上传资料构造器：按字段名/段号赋值，避免拼错顺序。</summary>
    public sealed class AcbelInsertDataBuilder
    {
        private readonly string[] _values = new string[AcbelInsertDataSpec.FieldCount];

        public AcbelInsertDataBuilder Set(int index1Based, string value)
        {
            AcbelInsertDataField f = AcbelInsertDataSpec.ByIndex(index1Based);
            if (f == null) throw new ArgumentOutOfRangeException("index1Based", "段号必须在 1.." + AcbelInsertDataSpec.FieldCount + " 之间");
            _values[index1Based - 1] = value ?? string.Empty;
            return this;
        }

        public AcbelInsertDataBuilder Set(string fieldName, string value)
        {
            AcbelInsertDataField f = AcbelInsertDataSpec.ByName(fieldName);
            if (f == null) throw new ArgumentException("未知字段名：" + fieldName, "fieldName");
            _values[f.Index - 1] = value ?? string.Empty;
            return this;
        }

        public AcbelInsertDataBuilder SetResult(bool pass, string failReason = null, string failValue = null, string errorCode = null)
        {
            Set(1, pass ? "PASS" : "FAIL");
            if (failReason != null) Set(2, failReason);
            if (failValue != null) Set(3, failValue);
            if (errorCode != null) Set(26, errorCode);
            return this;
        }

        /// <summary>拼成逗号分隔的上传资料字符串（未赋值的段留空）。</summary>
        public string Build()
        {
            return string.Join(",", _values.Select(v => v ?? string.Empty));
        }

        public AcbelPayloadValidationResult Validate(bool allowExtraFields = false)
        {
            return AcbelInsertDataValidator.Validate(Build(), allowExtraFields);
        }
    }
}
