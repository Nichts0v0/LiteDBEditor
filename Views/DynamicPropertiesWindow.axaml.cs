using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.ViewModels;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace LiteDBEditor.Views;

public partial class DynamicPropertiesWindow : Window
{
    #region 生命周期与初始化

    public DynamicPropertiesWindow()
    {
        InitializeComponent();
    }

    #endregion

    #region 保存与取消

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DynamicPropertiesViewModel vm)
        {
            vm.ClearErrors();
            // 执行保存回调（含查重校验），如果校验失败则不关闭窗口
            if (!await vm.ExecuteSaveAsync())
            {
                return;
            }
        }
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    #endregion

    #region 数值输入框字符过滤（Loaded 事件挂载）

    /// <summary>
    /// 数值 TextBox 初始化完成后，通过 Tunnel 事件拦截非法字符输入。
    /// - Int32 / Int64：只允许数字和首位负号
    /// - Double：允许小数点（最多 1 个）和首位负号
    /// TypeName 来自行的 DynamicPropertyItemViewModel。
    /// </summary>
    private void OnNumericTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not DynamicPropertyItemViewModel vm) return;

        var typeName = vm.TypeName; // 运行时取类型，闭包固定

        tb.AddHandler(
            InputElement.TextInputEvent,
            (object? s, TextInputEventArgs ev) =>
            {
                if (ev.Text == null) return;
                var box = (TextBox)s!;
                foreach (char c in ev.Text)
                {
                    bool ok = typeName switch
                    {
                        "Int32" or "Int64" =>
                            char.IsDigit(c)
                            || (c == '-' && (box.Text?.Length == 0 || box.SelectionStart == 0)),
                        "Double" =>
                            char.IsDigit(c)
                            || (c == '-' && (box.Text?.Length == 0 || box.SelectionStart == 0))
                            || (c == '.' && box.Text?.Contains('.') != true),
                        _ => true
                    };
                    if (!ok) { ev.Handled = true; return; }
                }
            },
            RoutingStrategies.Tunnel);
    }

    #endregion

    #region 复杂类型条目编辑弹窗入口

    /// <summary>
    /// 当用户点击字段行上的 [...] 按钮时触发（字段类型为 Array / Dictionary / Document）。
    /// 根据 SchemaProperty 决定弹窗模式并打开 CollectionEditorWindow。
    /// </summary>
    private async void OnEditComplexValue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not DynamicPropertyItemViewModel itemVm) return;

        var schema = itemVm.PropertySchema;
        if (schema == null) return;

        var parentVm = DataContext as DynamicPropertiesViewModel;
        if (parentVm == null) return;

        BsonDocument? parentDoc = parentVm.GetTargetDocument();
        if (parentDoc == null) return;

        var fieldName = itemVm.PropertyName;

        // 直接使用 ViewModel 已存储的原始 BsonValue 引用，避免二次查询导致数据为空
        var currentBson = itemVm.CurrentBsonValue ?? BsonValue.Null;

        var colEditorVm = new CollectionEditorViewModel();

        switch (schema.TypeName)
        {
            case "Array":
                {
                    BsonArray arr;
                    if (currentBson.IsArray)
                        arr = currentBson.AsArray;
                    else
                    {
                        arr = new BsonArray();
                        parentDoc[fieldName] = arr;
                    }

                    var elemSchema = schema.ElementSchema ?? new SchemaProperty
                    {
                        Name = "item",
                        DisplayName = "项目",
                        TypeName = "String"
                    };

                    colEditorVm.InitializeAsArray(arr, new SchemaProperty
                    {
                        Name = schema.Name,
                        DisplayName = schema.DisplayName,
                        TypeName = schema.TypeName,
                        ElementSchema = elemSchema
                    });
                    break;
                }

            case "Dictionary":
                {
                    BsonDocument dict;
                    if (currentBson.IsDocument)
                        dict = currentBson.AsDocument;
                    else
                    {
                        dict = new BsonDocument();
                        parentDoc[fieldName] = dict;
                    }

                    var elemSchema = schema.ElementSchema ?? new SchemaProperty
                    {
                        Name = "value",
                        DisplayName = "值",
                        TypeName = "String"
                    };
                    colEditorVm.InitializeAsDictionary(dict, new SchemaProperty
                    {
                        Name = schema.Name,
                        DisplayName = schema.DisplayName,
                        TypeName = "Dictionary",
                        ElementSchema = elemSchema
                    });
                    break;
                }

            case "Document":
                {
                    BsonDocument doc;
                    if (currentBson.IsDocument)
                        doc = currentBson.AsDocument;
                    else
                    {
                        doc = new BsonDocument();
                        parentDoc[fieldName] = doc;
                    }

                    if (schema.NestedProperties != null && schema.NestedProperties.Count > 0)
                    {
                        var sub = new SchemaData
                        {
                            TargetName = schema.DisplayName,
                            Properties = schema.NestedProperties
                        };
                        var subVm = new DynamicPropertiesViewModel();
                        subVm.LoadDocumentMetadata(doc, sub, (_) => Task.FromResult(true));
                        await new DynamicPropertiesWindow { DataContext = subVm }.ShowDialog(this);
                        // 刷新当前窗口的预览文字
                        itemVm.RefreshComplexPreview();
                        return;
                    }

                    colEditorVm.InitializeAsDocument(doc, schema);
                    break;
                }

            default:
                return;
        }

        var win = new CollectionEditorWindow { DataContext = colEditorVm };
        await win.ShowDialog(this);

        // 集合编辑窗关闭后刷新本行的预览文字（如"[3 项]"）
        itemVm.RefreshComplexPreview();
    }

    #endregion
}
