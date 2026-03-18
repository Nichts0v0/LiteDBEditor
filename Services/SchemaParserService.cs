using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LiteDB;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

/// <summary>
/// Schema 解析服务，提供多种方式推导数据结构（SchemaData）：
/// 1. 基于 JSON 样板推导。
/// 2. 基于现有的 BsonDocument 实例推导。
/// 3. 使用 Roslyn 静态分析 C# 源代码推导。
/// </summary>
public class SchemaParserService
{
    #region 基于 JSON 字符串的解析逻辑

    /// <summary>
    /// 从一段 JSON 字符串推导其对应的 Schema 特征，用于快速生成初步结构。
    /// </summary>
    /// <param name="targetName">目标类名或标识</param>
    /// <param name="jsonString">JSON 样例文本</param>
    /// <returns>推导出的 SchemaData 对象</returns>
    public SchemaData ParseFromJsonTemplate(string targetName, string jsonString)
    {
        var schemaData = new SchemaData { TargetName = targetName };

        try
        {
            var options = new JsonDocumentOptions { AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(jsonString, options);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new Exception("JSON 模板的根节点必须是一个 Object { }。");
            }

            schemaData.Properties = ParseObject(document.RootElement);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Parse JSON Schema Error: {ex.Message}");
        }

        return schemaData;
    }

    /// <summary>
    /// 递归解析 JsonElement 并将其转换为属性列表。
    /// </summary>
    private List<SchemaProperty> ParseObject(JsonElement element)
    {
        var properties = new List<SchemaProperty>();

        foreach (var prop in element.EnumerateObject())
        {
            var p = new SchemaProperty
            {
                Name = prop.Name,
                DisplayName = prop.Name, // 默认显示名字同程序名
                TypeName = MapJsonTypeToFriendlyString(prop.Value.ValueKind)
            };

            // 如果当前属性是对象，则递归深挖其嵌套结构
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                p.NestedProperties = ParseObject(prop.Value);
            }
            // 如果是数组，则通过第一个元素探索其元素类型（启发式推导）
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                var arrayElements = prop.Value.EnumerateArray();
                var enumerator = arrayElements.GetEnumerator();
                if (enumerator.MoveNext()) 
                {
                    var firstElement = enumerator.Current;
                    p.ElementSchema = new SchemaProperty
                    {
                        Name = "Item",
                        TypeName = MapJsonTypeToFriendlyString(firstElement.ValueKind)
                    };

                    // 如果列表内部又是复杂对象
                    if (firstElement.ValueKind == JsonValueKind.Object)
                    {
                        p.ElementSchema.NestedProperties = ParseObject(firstElement);
                    }
                }
                else
                {
                    // 对于空数组，默认退化为字符串数组
                    p.ElementSchema = new SchemaProperty { Name = "Item", TypeName = "String" };
                }
            }

            properties.Add(p);
        }

        return properties;
    }

    /// <summary>
    /// 将 JSON 数据类型映射为编辑器友好的类型名称字符串。
    /// </summary>
    private string MapJsonTypeToFriendlyString(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.String => "String",
            JsonValueKind.Number => "Double",
            JsonValueKind.True => "Boolean",
            JsonValueKind.False => "Boolean",
            JsonValueKind.Object => "Document",
            JsonValueKind.Array => "Array",
            _ => "String"
        };
    }

    #endregion

    #region 基于 BsonDocument 的解析逻辑

    /// <summary>
    /// 直接从现有的 BsonDocument 实例中反向推导 Schema 结构。
    /// 适用于在没有 Schema 定义的情况下编辑已有数据。
    /// </summary>
    /// <param name="targetName">目标标识</param>
    /// <param name="document">BsonDocument 实例</param>
    /// <returns>推导出的 SchemaData 对象</returns>
    public SchemaData ParseFromBsonDocument(string targetName, BsonDocument document)
    {
        var schemaData = new SchemaData { TargetName = targetName };
        schemaData.Properties = ParseBsonElements(document);
        return schemaData;
    }

    /// <summary>
    /// 递归解析 BsonDocument 的所有元素。
    /// </summary>
    private List<SchemaProperty> ParseBsonElements(BsonDocument document)
    {
        var properties = new List<SchemaProperty>();

        foreach (var kvp in document)
        {
            var p = new SchemaProperty
            {
                Name = kvp.Key,
                DisplayName = kvp.Key,
                TypeName = MapBsonTypeToFriendlyString(kvp.Value.Type)
            };

            if (kvp.Value.IsDocument)
            {
                p.NestedProperties = ParseBsonElements(kvp.Value.AsDocument);
            }
            else if (kvp.Value.IsArray)
            {
                var array = kvp.Value.AsArray;
                if (array.Count > 0)
                {
                    var firstElement = array[0];
                    p.ElementSchema = new SchemaProperty
                    {
                        Name = "Item",
                        TypeName = MapBsonTypeToFriendlyString(firstElement.Type)
                    };

                    if (firstElement.IsDocument)
                    {
                        p.ElementSchema.NestedProperties = ParseBsonElements(firstElement.AsDocument);
                    }
                }
                else
                {
                    p.ElementSchema = new SchemaProperty { Name = "Item", TypeName = "String" };
                }
            }

            properties.Add(p);
        }

        return properties;
    }

    /// <summary>
    /// 将 BsonType 映射为编辑器友好的类型名称字符串。
    /// </summary>
    private string MapBsonTypeToFriendlyString(BsonType kind)
    {
        return kind switch
        {
            BsonType.String => "String",
            BsonType.Int32 => "Int32",
            BsonType.Int64 => "Int64",
            BsonType.Double => "Double",
            BsonType.Decimal => "Double",
            BsonType.ObjectId => "ObjectId",
            BsonType.Guid => "String",
            BsonType.Boolean => "Boolean",
            BsonType.Document => "Document",
            BsonType.Array => "Array",
            _ => "String"
        };
    }

    #endregion

    #region 基于 C# 脚本文件的解析逻辑 (Roslyn AST)

    /// <summary>
    /// 使用 Roslyn 静态分析 C# 源码，提取指定类的结构作为 Schema。
    /// 这是最强大的推导方式，支持继承、泛型列表和嵌套类。
    /// </summary>
    /// <param name="codeText">C# 源代码</param>
    /// <param name="className">目标类名</param>
    /// <returns>推导出的 SchemaData 对象，若失败则返回 null</returns>
    public SchemaData? ParseFromCSharpSyntax(string codeText, string className)
    {
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(codeText);
            var root = syntaxTree.GetCompilationUnitRoot();

            var allClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            // 寻找对应的类声明节点：支持不区分大小写的匹配（应对数据库名与类名的大小写差异）
            var classDeclaration = allClasses
                .FirstOrDefault(c => string.Equals(c.Identifier.Text, className, StringComparison.OrdinalIgnoreCase));

            // 如果没找到同名的类，则默认抓取文件中的第一个类定义
            if (classDeclaration == null)
            {
                classDeclaration = allClasses.FirstOrDefault();
                if (classDeclaration == null) return null;
            }

            var schemaData = new SchemaData { TargetName = className };
            schemaData.Properties = ParseClassMembers(classDeclaration, allClasses);

            // 强制检查主键定义：如果 Schema 中没定义 _id，则自动补充一个，以防编辑器无法正确定位文档
            if (!schemaData.Properties.Any(p => p.Name == "_id"))
            {
                schemaData.Properties.Insert(0, new SchemaProperty
                {
                    Name = "_id",
                    DisplayName = "_id",
                    TypeName = "String" 
                });
            }

            return schemaData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Parse C# Schema Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 递归解析类成员，包括基类成员、属性和字段。
    /// </summary>
    private List<SchemaProperty> ParseClassMembers(ClassDeclarationSyntax classNode, List<ClassDeclarationSyntax> allClasses, HashSet<string>? processedClasses = null)
    {
        var properties = new List<SchemaProperty>();
        processedClasses ??= new HashSet<string>();

        var currentClassName = classNode.Identifier.Text;
        if (processedClasses.Contains(currentClassName)) return properties;
        processedClasses.Add(currentClassName);

        // 1. 处理基类继承：递归抓取父类中定义的公开成员
        if (classNode.BaseList != null)
        {
            foreach (var baseType in classNode.BaseList.Types)
            {
                string? baseClassName = null;
                if (baseType.Type is SimpleNameSyntax simpleName)
                {
                    baseClassName = simpleName.Identifier.Text;
                }
                else if (baseType.Type is QualifiedNameSyntax qualifiedName)
                {
                    baseClassName = qualifiedName.Right.Identifier.Text;
                }

                if (!string.IsNullOrEmpty(baseClassName))
                {
                    var baseClassNode = allClasses
                        .FirstOrDefault(c => string.Equals(c.Identifier.Text, baseClassName, StringComparison.OrdinalIgnoreCase));

                    if (baseClassNode != null)
                    {
                        properties.AddRange(ParseClassMembers(baseClassNode, allClasses, processedClasses));
                    }
                }
            }
        }

        // 2. 收集当前类定义的公开属性 (Properties)
        var propertyDeclarations = classNode.Members.OfType<PropertyDeclarationSyntax>();
        foreach (var prop in propertyDeclarations)
        {
            var p = CreateSchemaPropertyFromType(prop.Identifier.Text, prop.Type, allClasses, processedClasses);
            if (!properties.Any(existing => existing.Name == p.Name))
            {
                properties.Add(p);
            }
        }

        // 3. 收集当前类定义的公开字段 (Fields)
        var fieldDeclarations = classNode.Members.OfType<FieldDeclarationSyntax>();
        foreach (var field in fieldDeclarations)
        {
            if (field.Declaration.Variables.FirstOrDefault() is VariableDeclaratorSyntax variable)
            {
                var p = CreateSchemaPropertyFromType(variable.Identifier.Text, field.Declaration.Type, allClasses, processedClasses);
                if (!properties.Any(existing => existing.Name == p.Name))
                {
                    properties.Add(p);
                }
            }
        }

        return properties;
    }

    /// <summary>
    /// 根据 Roslyn 语法树中的类型节点构建 SchemaProperty。
    /// </summary>
    private SchemaProperty CreateSchemaPropertyFromType(string name, TypeSyntax typeSyntax, List<ClassDeclarationSyntax> allClasses, HashSet<string> processedClasses)
    {
        var p = new SchemaProperty
        {
            Name = name,
            DisplayName = name
        };

        if (typeSyntax is GenericNameSyntax genericName)
        {
            var baseType = genericName.Identifier.Text; // 例如 List, Dictionary

            if (baseType == "List" || baseType == "IEnumerable" || baseType == "IList")
            {
                p.TypeName = "Array";
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                {
                    p.ElementSchema = CreateSchemaPropertyFromType("Item", typeArg, allClasses, processedClasses);
                }
            }
            else if (baseType == "Dictionary" || baseType == "IDictionary")
            {
                p.TypeName = "Dictionary"; 
                p.CSharpTypeName = baseType;
                if (genericName.TypeArgumentList.Arguments.Skip(1).FirstOrDefault() is TypeSyntax valTypeArg)
                {
                    // 获取 Value 的类型 Schema
                    p.ElementSchema = CreateSchemaPropertyFromType("ValueItem", valTypeArg, allClasses, processedClasses);
                }
            }
            else
            {
                p.TypeName = "Document"; // 未知泛型默认作为子文档处理
            }
        }
        else if (typeSyntax is ArrayTypeSyntax arrayType)
        {
            p.TypeName = "Array";
            p.CSharpTypeName = arrayType.ElementType.ToString() + "[]";
            p.ElementSchema = CreateSchemaPropertyFromType("Item", arrayType.ElementType, allClasses, processedClasses);
        }
        else if (typeSyntax is NullableTypeSyntax nullableType)
        {
            // 处理可空类型（如 int?），剥离问号后按原始类型解析
            return CreateSchemaPropertyFromType(name, nullableType.ElementType, allClasses, processedClasses);
        }
        else if (typeSyntax is PredefinedTypeSyntax predefined)
        {
            p.TypeName = MapCSharpTypeToFriendlyString(predefined.Keyword.Text);
            p.CSharpTypeName = predefined.Keyword.Text;
        }
        else if (typeSyntax is IdentifierNameSyntax identifier)
        {
            var typeName = identifier.Identifier.Text;

            // 识别常见的系统内置类
            if (typeName == "String" || typeName == "DateTime" || typeName == "Guid")
            {
                p.TypeName = "String";
            }
            else
            {
                // 自定义类或结构：在当前源码树中寻找其定义
                p.TypeName = "Document";
                p.CSharpTypeName = typeName;
                var nestedClass = allClasses
                    .FirstOrDefault(c => c.Identifier.Text == typeName);

                if (nestedClass != null)
                {
                    // 递归挖掘嵌套模型结构
                    p.NestedProperties = ParseClassMembers(nestedClass, allClasses, new HashSet<string>(processedClasses));
                }
            }
        }
        else
        {
            p.TypeName = "String"; // 最终兜底类型
        }

        return p;
    }

    /// <summary>
    /// 将 C# 关键字映射为编辑器友好的类型名称字符串。
    /// </summary>
    private string MapCSharpTypeToFriendlyString(string keyword)
    {
        return keyword switch
        {
            "string" => "String",
            "int" => "Int32",
            "long" => "Int64",
            "short" => "Int32",
            "byte" => "Int32",
            "float" => "Double",
            "double" => "Double",
            "decimal" => "Double",
            "bool" => "Boolean",
            "object" => "Document",
            _ => "String"
        };
    }

    /// <summary>
    /// 严格应用 Schema：确保 document 中的字段符合 schema 定义，移除多余字段。
    /// 特性：如果文档刚好缺失一个字段且多出一个字段，则自动识别为“重命名”并迁移数据。
    /// </summary>
    /// <param name="document">要校验和清理的 BsonDocument</param>
    /// <param name="schema">参照的 Schema 结构</param>
    /// <returns>文档是否被修改（发生了清理或迁移）</returns>
    public bool ApplySchemaStrictly(BsonDocument document, SchemaData schema)
    {
        bool modified = false;
        var allowedNames = new HashSet<string>(schema.Properties.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        
        var currentKeys = document.Keys.ToList();
        var extraKeys = currentKeys.Where(k => k != "_id" && !allowedNames.Contains(k)).ToList();
        var missingKeys = schema.Properties.Select(p => p.Name).Where(n => n != "_id" && !document.ContainsKey(n)).ToList();

        // --- 启发式重命名识别 ---
        if (extraKeys.Count == 1 && missingKeys.Count == 1)
        {
            var oldKey = extraKeys[0];
            var newKey = missingKeys[0];
            var val = document[oldKey];
            document.Remove(oldKey);
            document[newKey] = val;
            Console.WriteLine($"[SmartMigration] Field renamed: {oldKey} -> {newKey}");
            return true;
        }

        // --- 正常清理多余字段 ---
        foreach (var key in extraKeys)
        {
            document.Remove(key);
            modified = true;
            Console.WriteLine($"[SchemaStrict] Removed extra field: {key}");
        }
        
        return modified;
    }

    #endregion
}
