using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.ViewModels;
using LiteDBEditor.Services;
using Avalonia.Markup.Xaml;

namespace LiteDBEditor.Views;

/// <summary>
/// 通用集合编辑弹窗，支持 Array / Dictionary / Document 三种模式。
/// 由 DynamicPropertiesWindow 的 OnEditComplexValue_Click 调起。
/// </summary>
public partial class CollectionEditorWindow : Window
{
    #region 生命周期与初始化

    public CollectionEditorWindow()
    {
        InitializeComponent();
    }

    #endregion

    #region 内部辅助 — 获取 ViewModel

    private CollectionEditorViewModel? Vm => DataContext as CollectionEditorViewModel;

    #endregion

    #region 添加条目

    private async void OnAddItemClick(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        var elemSchema = vm.ElementSchema;
        var elemType = elemSchema?.TypeName ?? "String";

        if (vm.EditMode == "Array")
        {
            if (elemType == "Document")
            {
                // 元素是嵌套类：弹出子窗口让用户填写各字段
                await OpenSubDocumentEditor(elemSchema!, null, (newDoc) =>
                {
                    vm.GetBackingArray()?.Add(newDoc);
                    vm.AddPrimitiveItemCommand.Execute(""); // 触发刷新（后面会直接刷）
                    // 由于我们直接改了 backing array，需要重新初始化
                    vm.InitializeAsArray(vm.GetBackingArray()!, elemSchema!);
                });
            }
            else
            {
                // 基础类型：弹出简单文本输入框
                var input = await ShowPrimitiveInputDialog($"请输入新的 {elemType} 值：", elemType);
                if (input != null)
                    vm.AddPrimitiveItemCommand.Execute(input);
            }
        }
        else if (vm.EditMode == "Dictionary")
        {
            // 先让用户输入 key
            var key = await ShowPrimitiveInputDialog("请输入新条目的键 (Key)：", "String");
            if (string.IsNullOrWhiteSpace(key)) return;

            if (elemType == "Document" && elemSchema != null)
            {
                await OpenSubDocumentEditor(elemSchema, null, (newDoc) =>
                {
                    vm.AddDictItemDocument(key, newDoc);
                });
            }
            else
            {
                var val = await ShowPrimitiveInputDialog($"请输入键 \"{key}\" 的值 ({elemType})：", elemType);
                if (val != null)
                    vm.AddDictItemPrimitive(key, val);
            }
        }
    }

    #endregion

    #region 编辑已有条目

    private async void OnEditItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not CollectionItemRow row) return;
        var vm = Vm;
        if (vm == null) return;

        var elemSchema = vm.ElementSchema;
        var bval = row.BsonVal;

        if (bval != null && bval.IsDocument)
        {
            // 嵌套对象：打开子编辑面板，改完直接回写（因为 bval 是引用类型）
            // 如果 elemSchema 为空，OpenSubDocumentEditor 内部会负责动态推导
            var subPath = string.IsNullOrEmpty(vm.ContextPath) ? row.KeyLabel : $"{vm.ContextPath} > {row.KeyLabel}";
            await OpenSubDocumentEditor(elemSchema, bval.AsDocument, (_) => { }, subPath);
            // 刷新列表（不需要重新初始化逻辑，直接通过引用同步）
            vm.RefreshItems();
        }
        else if (bval != null && bval.IsArray)
        {
            // 子数组：递归打开
            var subVm = new CollectionEditorViewModel();
            // 优先使用 elemSchema 的 ElementSchema，缺失则退化为默认
            var subSchema = elemSchema?.ElementSchema ?? new SchemaProperty
            {
                Name = "Items",
                DisplayName = "子列表",
                TypeName = "Array",
                ElementSchema = new SchemaProperty { Name = "Item", TypeName = "String" }
            };
            var subPath = string.IsNullOrEmpty(vm.ContextPath) ? row.KeyLabel : $"{vm.ContextPath} > {row.KeyLabel}";
            subVm.InitializeAsArray(bval.AsArray, subSchema, subPath);
            var subWin = new CollectionEditorWindow { DataContext = subVm };
            await subWin.ShowDialog(this);
            vm.RefreshItems();
        }
        else
        {
            // 基础类型：弹输入框修改
            // 注意：typeName 必须取 elemSchema?.TypeName（元素的类型），
            // 而不能取 bval 的 BsonType，否则第二次编辑时类型信息丢失
            var elemTypeName = vm.ElementSchema?.TypeName ?? "String";
            var current = row.ValuePreview;
            var input = await ShowPrimitiveInputDialog($"修改值（当前：{current}）：", elemTypeName, current);
            if (input == null) return;

            if (vm.EditMode == "Array" && vm.GetBackingArray() != null)
            {
                if (int.TryParse(row.KeyLabel.Trim('[', ']'), out var idx))
                {
                    vm.GetBackingArray()![idx] = ConvertBson(input, elemTypeName);
                    vm.NotifyChanged();
                }
                vm.RefreshItems();
            }
            else if (vm.EditMode == "Dictionary" && row.DictKey != null)
            {
                vm.GetBackingDocument()![row.DictKey] = ConvertBson(input, elemTypeName);
                vm.NotifyChanged();
                vm.RefreshItems();
            }
        }
    }

    #endregion

    #region 删除条目

    private void OnDeleteItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not CollectionItemRow row) return;
        var vm = Vm;
        if (vm == null) return;

        if (vm.EditMode == "Array")
            vm.RemoveArrayItemCommand.Execute(row);
        else if (vm.EditMode == "Dictionary")
            vm.RemoveDictItemCommand.Execute(row);
    }

    #endregion

    #region 关闭

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    #endregion

    #region 辅助 — 打开子 Document 编辑器（递归）

    /// <summary>
    /// 打开一个嵌套文档的子编辑面板。
    /// 如果 doc 为 null，则新建一个空 BsonDocument。
    /// onFinished 在用户点 Save 后被调用，传入最终的 BsonDocument。
    /// </summary>
    private async Task OpenSubDocumentEditor(
        SchemaProperty? schema,
        BsonDocument? doc,
        System.Action<BsonDocument> onFinished,
        string? contextPath = null)
    {
        var targetDoc = doc ?? new BsonDocument();

        // 若 schema 是 Dictionary 值（其 NestedProperties 非空），用 DynamicPropertiesWindow
        if (schema?.NestedProperties != null && schema.NestedProperties.Count > 0)
        {
            var schemaData = new SchemaData
            {
                TargetName = schema.DisplayName,
                Properties = schema.NestedProperties
            };
            var subVm = new DynamicPropertiesViewModel();
            subVm.LoadDocumentMetadata(targetDoc, schemaData, (saved) =>
            {
                onFinished?.Invoke(saved);
                return Task.FromResult(true);
            }, contextPath);
            var subWin = new DynamicPropertiesWindow { DataContext = subVm };
            await subWin.ShowDialog(this);
        }
        else
        {
            // 没有详细 Schema，尝试从当前文档动态推导（保底逻辑）
            var parser = new SchemaParserService();
            var dynamicSchema = parser.ParseFromBsonDocument(schema?.DisplayName ?? "对象内容", targetDoc);

            var subVm = new DynamicPropertiesViewModel();
            subVm.LoadDocumentMetadata(targetDoc, dynamicSchema, (saved) =>
            {
                onFinished?.Invoke(saved);
                return Task.FromResult(true);
            }, contextPath);
            var subWin = new DynamicPropertiesWindow { DataContext = subVm };
            await subWin.ShowDialog(this);

            // 重要：DynamicPropertiesViewModel 弹窗目前不返回是否编辑的状态，
            // 只要弹窗打开并点过保存回调，我们就认为它变动了。
            Vm?.NotifyChanged();
        }
    }

    #endregion

    #region 辅助 — 基础类型输入弹窗

    /// <summary>弹出一个简单对话框，让用户输入一个基础类型值，返回字符串或 null（取消）。</summary>
    private async Task<string?> ShowPrimitiveInputDialog(string prompt, string typeName, string defaultVal = "")
    {
        var dialog = new PrimitiveInputDialog(prompt, typeName, defaultVal);
        return await dialog.ShowDialog<string?>(this);
    }

    private static BsonValue ConvertBson(string raw, string typeName)
    {
        try
        {
            return typeName switch
            {
                "Boolean" => new BsonValue(bool.Parse(raw)),
                "Double" => new BsonValue(double.Parse(raw)),
                "Int32" => new BsonValue(int.TryParse(raw, out var i32) ? i32 : (int)(double.Parse(raw))),
                "Int64" => new BsonValue(long.TryParse(raw, out var i64) ? i64 : (long)(double.Parse(raw))),
                _ => new BsonValue(raw)
            };
        }
        catch { return new BsonValue(raw); }
    }

    #endregion
}
