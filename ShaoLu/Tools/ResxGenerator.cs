using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Resources;

namespace ShaoLu.Tools
{
    public static class ResxGenerator
    {
        /// <summary>
        /// 根据默认资源文件批量生成多语言 .resx 文件
        /// </summary>
        /// <param name="defaultResxPath">默认资源文件路径 (如 Resources/Strings.resx)</param>
        /// <param name="targetCultures">需要生成的目标语言列表</param>
        public static void GenerateLanguageFiles(string defaultResxPath, params string[] targetCultures)
        {
            if (!File.Exists(defaultResxPath))
            {
                throw new FileNotFoundException($"找不到默认资源文件: {defaultResxPath}");
            }

            string directory = Path.GetDirectoryName(defaultResxPath)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(defaultResxPath);

            // 读取默认资源文件中的所有键值对
            var defaultEntries = new List<DictionaryEntry>();
            using (var reader = new ResXResourceReader(defaultResxPath))
            {
                reader.UseResXDataNodes = true;
                foreach (DictionaryEntry entry in reader)
                {
                    defaultEntries.Add(entry);
                }
            }

            // 为每种目标语言生成 .resx 文件
            foreach (var cultureName in targetCultures)
            {
                string targetPath = Path.Combine(directory, $"{fileNameWithoutExt}.{cultureName}.resx");

                // 如果文件已存在，则跳过（防止覆盖已翻译的内容）
                if (File.Exists(targetPath)) continue;

                using (var writer = new ResXResourceWriter(targetPath))
                {
                    writer.BasePath = directory;
                    foreach (var entry in defaultEntries)
                    {
                        var node = (ResXDataNode)entry.Value;
                        // 写入键名，值保留为默认文本（方便开发者后续替换）
                        writer.AddResource(node.Name, node.GetValue((ITypeResolutionService)null!));
                    }
                }
                System.Diagnostics.Debug.WriteLine($"成功生成资源文件: {targetPath}");
            }
        }
    }
}
