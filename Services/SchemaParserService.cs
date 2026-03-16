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
/// 提供基于 JSON 样板数据推导 SchemaData 元数据的服务
/// </summary>
public class SchemaParserService
{
    #region 基于 JSON 字符串的解析逻辑

    /// <summary>
    /// 从一段 JSON 字符串推导其对应的 Schema 特征
    /// </summary>
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

            // 如果这一层还是个对象，递归深挖嵌套
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                p.NestedProperties = ParseObject(prop.Value);
            }
            // 如果是个数组，探索其第一个元素的类型（假设数组元素具有相同结构）
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                var arrayElements = prop.Value.EnumerateArray();
                var enumerator = arrayElements.GetEnumerator();
                if (enumerator.MoveNext()) // 如果至少有一个元素
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
                    // 空数组默认当作存String的地方
                    p.ElementSchema = new SchemaProperty { Name = "Item", TypeName = "String" };
                }
            }

            properties.Add(p);
        }

        return properties;
    }

    private string MapJsonTypeToFriendlyString(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.String => "String",
            JsonValueKind.Number => "Double", // 简单处理为浮点数，视UI需求可区分Int
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
    /// 直接从现有的 BsonDocument 中推导 Schema，
    /// 解决使用模板前需要编辑已有数据的问题。
    /// </summary>
    public SchemaData ParseFromBsonDocument(string targetName, BsonDocument document)
    {
        var schemaData = new SchemaData { TargetName = targetName };
        schemaData.Properties = ParseBsonElements(document);
        return schemaData;
    }

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
            BsonType.Guid => "String", // GUID 在 UI 层按字符串编辑即可
            BsonType.Boolean => "Boolean",
            BsonType.Document => "Document",
            BsonType.Array => "Array",
            _ => "String"
        };
    }

    #endregion

    #region 基于 C# 脚本文件的解析逻辑 (Roslyn AST)

    /// <summary>
    /// 从一段 C# 源码文本中，提取指定类名的数据契约结构
    /// </summary>
    /// <param name="codeText">完整的 C# 源码字符串</param>
    /// <param name="className">需要匹配提取作为骨架的类名（对应表名）</param>
    public SchemaData? ParseFromCSharpSyntax(string codeText, string className)
    {
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(codeText);
            var root = syntaxTree.GetCompilationUnitRoot();

            var allClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            // 寻找对应的类声明节点：放宽为不区分大小写匹配（用户的表名可能首字母小写而类名是大写）
            var classDeclaration = allClasses
                .FirstOrDefault(c => string.Equals(c.Identifier.Text, className, StringComparison.OrdinalIgnoreCase));

            // 如果没找到同名的类，强行抓取文件里的第一个类充当该表的模型约束
            if (classDeclaration == null)
            {
                classDeclaration = allClasses.FirstOrDefault();
                if (classDeclaration == null) return null; // 连个类都没有则证明不是有效配置
            }

            var schemaData = new SchemaData { TargetName = className };
            schemaData.Properties = ParseClassMembers(classDeclaration, allClasses);

            // 如果这脚本没给出 _id 的定义，编辑器需要强行塞一个给它以防无法定位
            if (!schemaData.Properties.Any(p => p.Name == "_id"))
            {
                schemaData.Properties.Insert(0, new SchemaProperty
                {
                    Name = "_id",
                    DisplayName = "_id",
                    TypeName = "String" // 修改默认退化类型为 String，比 Double 更符合主键直觉
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

    private List<SchemaProperty> ParseClassMembers(ClassDeclarationSyntax classNode, List<ClassDeclarationSyntax> allClasses, HashSet<string>? processedClasses = null)
    {
        var properties = new List<SchemaProperty>();
        processedClasses ??= new HashSet<string>();

        var currentClassName = classNode.Identifier.Text;
        if (processedClasses.Contains(currentClassName)) return properties;
        processedClasses.Add(currentClassName);

        // 1. 处理基类继承 (Base Classes)
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
                    else
                    {
                        Console.WriteLine($"[Warning] Base class '{baseClassName}' not found in the current file for class '{currentClassName}'.");
                    }
                }
            }
        }

        // 2. 收集当前类的公开属性 (Properties)
        var propertyDeclarations = classNode.Members.OfType<PropertyDeclarationSyntax>();
        foreach (var prop in propertyDeclarations)
        {
            var p = CreateSchemaPropertyFromType(prop.Identifier.Text, prop.Type, allClasses, processedClasses);
            if (!properties.Any(existing => existing.Name == p.Name))
            {
                properties.Add(p);
            }
        }

        // 3. 收集当前类的公开字段 (Fields)
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

    private SchemaProperty CreateSchemaPropertyFromType(string name, TypeSyntax typeSyntax, List<ClassDeclarationSyntax> allClasses, HashSet<string> processedClasses)
    {
        var p = new SchemaProperty
        {
            Name = name,
            DisplayName = name
        };

        if (typeSyntax is GenericNameSyntax genericName)
        {
            var baseType = genericName.Identifier.Text; // e.g., List, Dictionary

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
                    // Value 的类型 Schema
                    p.ElementSchema = CreateSchemaPropertyFromType("ValueItem", valTypeArg, allClasses, processedClasses);
                }
            }
            else
            {
                p.TypeName = "Document"; // 未知泛型当文档搞
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
            // 对于 int? string? 剥去问号直接查底层类型
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

            // 如果它是个首字母大写的常见系统类 (String, DateTime等)
            if (typeName == "String" || typeName == "DateTime" || typeName == "Guid")
            {
                p.TypeName = "String";
            }
            else
            {
                // 自己定义的模型嵌套类，去找整个树里有没有对应 class 声明
                p.TypeName = "Document";
                p.CSharpTypeName = typeName;
                var nestedClass = allClasses
                    .FirstOrDefault(c => c.Identifier.Text == typeName);

                if (nestedClass != null)
                {
                    // 递归挖掘自定义模型
                    p.NestedProperties = ParseClassMembers(nestedClass, allClasses, new HashSet<string>(processedClasses));
                }
            }
        }
        else
        {
            p.TypeName = "String"; // 兜底
        }

        return p;
    }

    private string MapCSharpTypeToFriendlyString(string keyword)
    {
        return keyword switch
        {
            "string" => "String",
            "int" => "Int32",     // 保持整数类型独立以便 UI 限制输入
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
    /// 严格应用 Schema：移除 document 中不在 schema 定义范围内的所有多余字段。
    /// 启发式增强：如果文档中正好缺失一个字段，且多出一个字段，则视为重命名并迁移数据。
    /// </summary>
    public bool ApplySchemaStrictly(BsonDocument document, SchemaData schema)
    {
        bool modified = false;
        var allowedNames = new HashSet<string>(schema.Properties.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        
        var currentKeys = document.Keys.ToList();
        var extraKeys = currentKeys.Where(k => k != "_id" && !allowedNames.Contains(k)).ToList();
        var missingKeys = schema.Properties.Select(p => p.Name).Where(n => n != "_id" && !document.ContainsKey(n)).ToList();

        // --- 启发式重命名识别 ---
        // 如果文档中多出一个字段且脚本中也刚好缺失一个字段，我们认为发生了重命名，自动执行数据迁移
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
