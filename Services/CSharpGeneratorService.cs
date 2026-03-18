using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

/// <summary>
/// C# 代码生成与解析服务，用于根据结构定义生成 POCO 类，或从源码中逆向解析类定义。
/// </summary>
public class CSharpGeneratorService
{
    /// <summary>
    /// 根据类定义对象生成完整的 C# 源码。
    /// </summary>
    /// <param name="mainClass">主类定义信息</param>
    /// <returns>生成的 C# 源代码字符串</returns>
    public string GenerateCode(ClassDefinition mainClass)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using LiteDB;");
        sb.AppendLine();
        sb.AppendLine("namespace LiteDBEditor.Models;");
        sb.AppendLine();

        GenerateClass(sb, mainClass, true);

        return sb.ToString();
    }

    /// <summary>
    /// 递归生成 C# 类结构。
    /// </summary>
    /// <param name="sb">StringBuilder 实例</param>
    /// <param name="classDef">当前类定义</param>
    /// <param name="isMainClass">是否为入口主类（主类会强制生成 [BsonId]）</param>
    private void GenerateClass(StringBuilder sb, ClassDefinition classDef, bool isMainClass)
    {
        sb.AppendLine($"public class {classDef.ClassName}");
        sb.AppendLine("{");

        // 仅主类生成 _id 且固定为 string，用于满足 LiteDB 默认主键要求
        if (isMainClass && !classDef.Fields.Any(f => f.FieldName.Equals("_id", StringComparison.OrdinalIgnoreCase) || f.FieldName.Equals("Id", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("    [BsonId]");
            sb.AppendLine("    public string _id { get; set; }");
            sb.AppendLine();
        }

        foreach (var field in classDef.Fields)
        {
            var typeStr = GetTypeString(field.Type, field.CustomTypeName, field.SubType, field.SubCustomTypeName, field.KeyType, field.KeyCustomTypeName);
            sb.AppendLine($"    public {typeStr} {field.FieldName} {{ get; set; }}");
        }

        sb.AppendLine("}");

        // 递归生成所有内部嵌套类
        foreach (var inner in classDef.InnerClasses)
        {
            sb.AppendLine();
            GenerateClass(sb, inner, false);
        }
    }

    /// <summary>
    /// 将内部枚举类型转换为 C# 类型关键字或自定义类名。
    /// </summary>
    private string GetTypeString(FieldType type, string? custom, FieldType sub, string? subCustom, FieldType key, string? keyCustom)
    {
        return type switch
        {
            FieldType.Int => "int",
            FieldType.Float => "float",
            FieldType.Bool => "bool",
            FieldType.String => "string",
            FieldType.Custom => custom ?? "object",
            FieldType.List => $"List<{GetTypeString(sub, subCustom, FieldType.String, null, FieldType.String, null)}>",
            FieldType.Dictionary => $"Dictionary<{GetTypeString(key, keyCustom, FieldType.String, null, FieldType.String, null)}, {GetTypeString(sub, subCustom, FieldType.String, null, FieldType.String, null)}>",
            _ => "string"
        };
    }

    /// <summary>
    /// 解析 C# 源码字符串，提取出类及其字段定义。
    /// </summary>
    /// <param name="code">C# 源代码文本</param>
    /// <returns>解析出的主类定义对象</returns>
    public ClassDefinition ParseCode(string code)
    {
        ClassDefinition? mainClass = null;
        // 匹配类定义头：支持可选修饰符及 partial 关键字
        var headerRegex = new Regex(@"(?:(?:public|internal|private|protected)\s+)?(?:partial\s+)?class\s+(\w+)", RegexOptions.Multiline);
        var matches = headerRegex.Matches(code);

        foreach (Match match in matches)
        {
            var className = match.Groups[1].Value;
            int startBrace = code.IndexOf('{', match.Index + match.Length);
            if (startBrace == -1) continue;

            // 简单的花括号匹配逻辑，用于提取类体
            int braceCount = 1;
            int endIdx = -1;
            for (int i = startBrace + 1; i < code.Length; i++)
            {
                if (code[i] == '{') braceCount++;
                else if (code[i] == '}') braceCount--;
                if (braceCount == 0) { endIdx = i; break; }
            }

            if (endIdx == -1) continue;

            var classBody = code.Substring(startBrace + 1, endIdx - startBrace - 1);
            var currentClass = new ClassDefinition { ClassName = className };

            // 增强的正则表达式：匹配字段、属性及其可能的特性和修饰符
            var fieldRegex = new Regex(@"(?:\[[^\]]*\]\s*)?(?:(?:public|private|protected|internal|static|readonly|virtual|override|new|async|required)\s+)*([\w<, >\[\].]+?)\s+(\w+)\s*(?:\{[\s\S]*?\}|;|=[\s\S]*?;)", RegexOptions.Multiline);
            var fieldMatches = fieldRegex.Matches(classBody);

            foreach (Match m in fieldMatches)
            {
                var typeStr = m.Groups[1].Value.Trim();
                var name = m.Groups[2].Value;

                // 跳过主键字段，它在生成时会根据规则自动处理
                if (name == "_id") continue;

                var field = new FieldDefinition { FieldName = name };
                ParseTypeString(typeStr, field);
                currentClass.Fields.Add(field);
            }

            if (mainClass == null) mainClass = currentClass;
            else if (mainClass.ClassName != currentClass.ClassName) mainClass.InnerClasses.Add(currentClass);
        }

        return mainClass ?? new ClassDefinition();
    }

    /// <summary>
    /// 将 C# 类型字符串（如 List&lt;string&gt;）解析并填充到 FieldDefinition 中。
    /// </summary>
    private void ParseTypeString(string typeStr, FieldDefinition field)
    {
        typeStr = typeStr.Trim();

        // 核心修复：手动剥离可能混入类型字符串中的修饰符
        var modifiers = new[] { "public", "private", "protected", "internal", "static", "readonly", "virtual", "override", "new", "async", "required" };
        var typeParts = typeStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (typeParts.Length > 1)
        {
            var filteredParts = typeParts.Where(p => !modifiers.Contains(p.ToLowerInvariant())).ToList();
            if (filteredParts.Count > 0)
            {
                typeStr = string.Join(" ", filteredParts);
            }
        }

        // 剥离常见的系统命名空间前缀
        if (typeStr.StartsWith("System."))
        {
            if (!typeStr.Contains("<") && !typeStr.Contains("["))
                typeStr = typeStr.Substring(7);
        }

        string lowerType = typeStr.ToLowerInvariant();

        if (lowerType == "int" || lowerType == "int32" || lowerType == "system.int32")
            field.Type = FieldType.Int;
        else if (lowerType == "float" || lowerType == "single" || lowerType == "system.single")
            field.Type = FieldType.Float;
        else if (lowerType == "bool" || lowerType == "boolean" || lowerType == "system.boolean")
            field.Type = FieldType.Bool;
        else if (lowerType == "string" || lowerType == "system.string")
            field.Type = FieldType.String;
        else if (typeStr.EndsWith("[]"))
        {
            field.Type = FieldType.List;
            var baseType = typeStr.Substring(0, typeStr.Length - 2).Trim();
            var subField = new FieldDefinition();
            ParseTypeString(baseType, subField);
            field.SubType = subField.Type;
            field.SubCustomTypeName = subField.CustomTypeName;
        }
        else if (Regex.IsMatch(typeStr, @"^List\s*<"))
        {
            field.Type = FieldType.List;
            // 提取泛型参数内容
            var match = Regex.Match(typeStr, @"<([\s\S]+)>");
            if (match.Success)
            {
                var innerContent = match.Groups[1].Value.Trim();
                var subField = new FieldDefinition();
                ParseTypeString(innerContent, subField);
                field.SubType = subField.Type;
                field.SubCustomTypeName = subField.CustomTypeName;
            }
        }
        else if (Regex.IsMatch(typeStr, @"^Dictionary\s*<"))
        {
            field.Type = FieldType.Dictionary;
            var match = Regex.Match(typeStr, @"<([\s\S]+)>");
            if (match.Success)
            {
                var innerContent = match.Groups[1].Value.Trim();
                var parts = innerContent.Split(',', 2);
                if (parts.Length == 2)
                {
                    var keyField = new FieldDefinition();
                    ParseTypeString(parts[0].Trim(), keyField);
                    field.KeyType = keyField.Type;

                    var valField = new FieldDefinition();
                    ParseTypeString(parts[1].Trim(), valField);
                    field.SubType = valField.Type;
                    field.SubCustomTypeName = valField.CustomTypeName;
                }
            }
        }
        else
        {
            field.Type = FieldType.Custom;
            field.CustomTypeName = typeStr;
        }
    }
}
