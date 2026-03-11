using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

public class CSharpGeneratorService
{
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

    private void GenerateClass(StringBuilder sb, ClassDefinition classDef, bool isMainClass)
    {
        sb.AppendLine($"public class {classDef.ClassName}");
        sb.AppendLine("{");

        // 仅主类生成 _id 且固定为 string
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

        // 递归生成辅助类
        foreach (var inner in classDef.InnerClasses)
        {
            sb.AppendLine();
            GenerateClass(sb, inner, false);
        }
    }

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

    public ClassDefinition ParseCode(string code)
    {
        ClassDefinition? mainClass = null;
        // 匹配类定义头：修饰符可选，支持 partial
        var headerRegex = new Regex(@"(?:(?:public|internal|private|protected)\s+)?(?:partial\s+)?class\s+(\w+)", RegexOptions.Multiline);
        var matches = headerRegex.Matches(code);

        foreach (Match match in matches)
        {
            var className = match.Groups[1].Value;
            int startBrace = code.IndexOf('{', match.Index + match.Length);
            if (startBrace == -1) continue;

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

            // 修改：增强正则表达式
            // 1. 匹配特性
            // 2. 匹配修饰符（增加更多 C# 修饰符）
            // 3. 匹配类型（支持点号、空格、尖括号、方括号）
            // 4. 匹配名称
            // 5. 匹配结尾（{get;set;} 或分号或赋值）
            var fieldRegex = new Regex(@"(?:\[[^\]]*\]\s*)?(?:(?:public|private|protected|internal|static|readonly|virtual|override|new|async|required)\s+)*([\w<, >\[\].]+?)\s+(\w+)\s*(?:\{[\s\S]*?\}|;|=[\s\S]*?;)", RegexOptions.Multiline);
            var fieldMatches = fieldRegex.Matches(classBody);

            foreach (Match m in fieldMatches)
            {
                var typeStr = m.Groups[1].Value.Trim();
                var name = m.Groups[2].Value;

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

    private void ParseTypeString(string typeStr, FieldDefinition field)
    {
        typeStr = typeStr.Trim();

        // 核心修复：如果修饰符漏网进入了类型字符串，在此处强制剥离
        // 匹配字段可能带有的所有常见修饰符，并只取最后一部分作为类型
        var modifiers = new[] { "public", "private", "protected", "internal", "static", "readonly", "virtual", "override", "new", "async", "required" };
        var typeParts = typeStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (typeParts.Length > 1)
        {
            // 过滤掉所有修饰符，保留剩余部分（通常是最后一个或带尖括号的整体）
            var filteredParts = typeParts.Where(p => !modifiers.Contains(p.ToLowerInvariant())).ToList();
            if (filteredParts.Count > 0)
            {
                typeStr = string.Join(" ", filteredParts);
            }
        }

        // 预处理：去掉常见的命名空间前缀 (简单处理)
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
            // 提取 < > 内部内容
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
