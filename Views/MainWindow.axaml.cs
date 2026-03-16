using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.Services;
using LiteDBEditor.ViewModels;

namespace LiteDBEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // 终极确认方案：使用 Tunnel 策略。此事件会在按钮响应点击前优先触发。
        // handledEventsToo: true 确保即使点击在按钮 or 滚动条上，我们的逻辑也能执行。
        this.AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel, true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SchemaLoaded -= OnSchemaLoaded;
            vm.SchemaLoaded += OnSchemaLoaded;
        }
    }

    #region 列模板生成

    private void OnSchemaLoaded(object? sender, SchemaData schemaData)
    {
        if (MainDataGrid == null) return;

        // 使用 Dispatcher 确保在 UI 线程执行，并降低优先级防止与布局冲突
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Console.WriteLine($"[UI] Rebuilding columns for: {schemaData.TargetName}");

                // 暂时断开数据源绑定，防止列变动时触发不必要的渲染计算
                var oldItemsSource = MainDataGrid.ItemsSource;
                MainDataGrid.ItemsSource = null;

                // 保留第一列 "Actions" 按钮列，清除其它生成的动态列
                while (MainDataGrid.Columns.Count > 1)
                    MainDataGrid.Columns.RemoveAt(1);

                foreach (var prop in schemaData.Properties)
                {
                    // 复杂类型（数组/字典/嵌套类）
                    var isComplex = prop.TypeName is "Array" or "Dictionary" or "Document";
                    var isReadOnly = false;

                    var capturedProp = prop;

                    var column = new DataGridTemplateColumn
                    {
                        Header = $"{prop.DisplayName} ({prop.GetFriendlyTypeString()})",
                        Tag = prop.Name,
                        IsReadOnly = isReadOnly,
                        CanUserSort = !isComplex,
                        SortMemberPath = isComplex ? null : $"[{prop.Name}]"
                    };

                    // ---- CellTemplate ----
                    var cellTemplate = new FuncDataTemplate<BsonDocumentWrapper>((data, _) =>
                    {
                        if (data == null) return null;
                        var textBlock = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(5, 0), TextTrimming = isComplex ? Avalonia.Media.TextTrimming.CharacterEllipsis : Avalonia.Media.TextTrimming.None };
                        textBlock.Bind(TextBlock.TextProperty, new Binding($"[{capturedProp.Name}]"));
                        var colorBinding = new MultiBinding { Converter = Converters.ModifiedFieldColorConverter.Instance, ConverterParameter = capturedProp.Name };
                        colorBinding.Bindings.Add(new Binding("."));
                        colorBinding.Bindings.Add(new Binding("IsModified"));
                        textBlock.Bind(TextBlock.ForegroundProperty, colorBinding);
                        return textBlock;
                    }, true);
                    column.CellTemplate = cellTemplate;

                    if (!isReadOnly)
                    {
                        var capturedTypeName = capturedProp.TypeName;
                        var editingTemplate = new FuncDataTemplate<BsonDocumentWrapper>((data, _) =>
                        {
                            var textBox = new TextBox { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = isComplex ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap };
                            textBox.Bind(TextBox.TextProperty, new Binding($"[{capturedProp.Name}]") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, Mode = BindingMode.OneWay });
                            if (capturedTypeName is "Int32" or "Int64" or "Double")
                            {
                                textBox.AddHandler(InputElement.TextInputEvent, (object? s, TextInputEventArgs ev) =>
                                {
                                    if (ev.Text == null) return;
                                    var tb = (TextBox)s!;
                                    foreach (char c in ev.Text)
                                    {
                                        bool ok = capturedTypeName switch
                                        {
                                            "Int32" or "Int64" => char.IsDigit(c) || (c == '-' && (tb.Text?.Length == 0 || tb.SelectionStart == 0)),
                                            "Double" => char.IsDigit(c) || (c == '-' && (tb.Text?.Length == 0 || tb.SelectionStart == 0)) || (c == '.' && tb.Text?.Contains('.') != true),
                                            _ => true
                                        };
                                        if (!ok) { ev.Handled = true; return; }
                                    }
                                }, RoutingStrategies.Tunnel);
                            }

                            return textBox;
                        }, true);
                        column.CellEditingTemplate = editingTemplate;
                    }
                    MainDataGrid.Columns.Add(column);
                }

                // 恢复数据源
                MainDataGrid.ItemsSource = oldItemsSource;
                Console.WriteLine($"[UI] Finished rebuilding columns for: {schemaData.TargetName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] OnSchemaLoaded UI update failed: {ex.Message}");
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    #endregion

    #region 点击处理

    /// <summary>
    /// 点击 DataGrid 表头或空白处时提交当前行编辑并清除选中
    /// </summary>
    private void OnDataGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 若点击目标不是 DataGridCell / DataGridRow，则视为点击了空白区域
        var control = e.Source as Avalonia.Controls.Control;
        if (control == null) return;

        // 检查是否点中了行内的任意单元格元素
        bool hitRow = false;
        var current = control as Avalonia.Visual;
        while (current != null)
        {
            if (current is DataGridCell or DataGridRow)
            {
                hitRow = true;
                break;
            }
            current = current.Parent as Avalonia.Visual;
        }

        if (!hitRow)
        {
            // 点击了表头/空白区域：提交编辑并取消选中
            MainDataGrid.CommitEdit();
            MainDataGrid.SelectedItem = null;
        }
    }

    /// <summary>
    /// 全局点击处理（Tunnel 策略）：当点击非当前编辑区域时，强制提交 Grid 更改。
    /// </summary>
    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var control = e.Source as Avalonia.Controls.Control;
        if (control == null) return;

        // 1. 检查点击源是否是 DataGrid 的内部组成部分
        bool hitGrid = false;
        var current = control as Avalonia.Visual;
        while (current != null)
        {
            if (current == MainDataGrid)
            {
                hitGrid = true;
                break;
            }
            current = current.Parent as Avalonia.Visual;
        }

        if (!hitGrid)
        {
            // 点击了外部（侧边栏、按钮等）：执行提交（由于是 Tunnel，会在按钮 Click 前完成）
            MainDataGrid.CommitEdit();
            this.Focus();
        }
    }

    /// <summary>
    /// 当用户开始编辑单元格时，清除旧的全局错误提示
    /// </summary>
    private void OnDataGridBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearGridError();
        }
    }

    /// <summary>
    /// 当用户直接在 DataGrid 单元格编辑结束时触发。
    /// 用于拦截 _id 列的修改并进行唯一性校验。
    /// </summary>
    private void OnDataGridCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // 仅处理提交动作（而非取消）
        if (e.EditAction != DataGridEditAction.Commit) return;

        // 通过 Tag 获取当前列绑定的字段名
        var propName = e.Column.Tag?.ToString();
        if (string.IsNullOrEmpty(propName)) return;

        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        // 提取输入框中的新字符串值
        if (e.EditingElement is TextBox textBox)
        {
            var wrapper = e.Row.DataContext as BsonDocumentWrapper;
            if (wrapper != null)
            {
                var newText = textBox.Text;
                var oldText = wrapper[propName];

                // 如果值没变，直接返回
                if (string.Equals(newText, oldText)) return;

                // --- 特殊校验：ID 字段 ---
                if (propName == "_id")
                {
                    var newVal = wrapper.ConvertToBsonValue(newText, wrapper.GetRawValue("_id").Type);

                    // 1. 校验非空
                    if (newVal.IsNull || (newVal.IsString && string.IsNullOrWhiteSpace(newVal.AsString)))
                    {
                        e.Cancel = true;
                        textBox.Text = oldText; // 强制 UI 复位
                        vm.GridErrorMessage = "修改失败：ID 不能为空。";
                        return;
                    }

                    // 2. 校验唯一性
                    if (vm.IsIdDuplicate(newVal, wrapper))
                    {
                        e.Cancel = true;
                        textBox.Text = oldText; // 强制 UI 复位
                        vm.GridErrorMessage = $"修改失败：ID '{newVal}' 冲突。";
                        return;
                    }

                    // 校验并写入模型
                    vm.ClearGridError();
                    wrapper[propName] = newText;
                }
                else
                {
                    // 非 ID 字段，直接手动写入模型（因为绑定是 OneWay）
                    wrapper.ClearAllErrors();
                    vm.ClearGridError();
                    wrapper[propName] = newText;
                }
            }
        }
    }

    #endregion

    #region 数据库操作

    private async void OnOpenDatabaseClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 LiteDB 数据库",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("LiteDB Files") { Patterns = new[] { "*.db" } },
                new FilePickerFileType("All Files")    { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count >= 1)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null) vm.OpenDatabase(path);
        }
    }

    private async void OnNewDatabaseClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "新建 LiteDB 数据库",
            DefaultExtension = "db",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("LiteDB Files") { Patterns = new[] { "*.db" } }
            }
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null) vm.OpenDatabase(path);
        }
    }

    #endregion

    #region 文档编辑弹窗

    private async void OnEditDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not BsonDocumentWrapper wrapper) return;

        var vm = DataContext as MainWindowViewModel;
        if (vm == null || string.IsNullOrEmpty(vm.SelectedCollection)) return;

        var parser = new SchemaParserService();
        var schema = vm.CurrentSchema ?? parser.ParseFromBsonDocument(vm.SelectedCollection, wrapper.Document);

        // 使用 GetMergedDocument 确保 _pendingChanges 里最新的数据也被包含在克隆中
        var clonedBson = LiteDB.JsonSerializer
            .Deserialize(LiteDB.JsonSerializer.Serialize(wrapper.GetMergedDocument()))
            .AsDocument;

        var dialogVm = new DynamicPropertiesViewModel();
        var originalId = wrapper.GetRawValue("_id"); // 记录原始 ID 用于回滚

        // 提供实时查重逻辑
        dialogVm.GlobalIdDuplicateCheckFunc = (propName, bsonVal) => 
        {
            if (propName == "_id") return vm.IsIdDuplicate(bsonVal, wrapper);
            return false;
        };

        dialogVm.LoadDocumentMetadata(clonedBson, schema, (updatedBson) =>
        {
            // 在此阶段，VM 已经通过其内部的 Validate 和回退逻辑保证了数据的业务合法性（非空、查重等）
            // 我们只需要简单执行物理写回并返回 true 即可
            foreach (var kvp in updatedBson)
            {
                wrapper.SetRawValueAndNotify(kvp.Key, kvp.Value);
            }
            return Task.FromResult(true);
        }, $"Row[{originalId}]");

        await new DynamicPropertiesWindow { DataContext = dialogVm }.ShowDialog(this);
        // 保存回调已在 OnSaveClick 里执行，显式刷新整行数据
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => wrapper.RefreshAll(),
            Avalonia.Threading.DispatcherPriority.Render);
    }

    private async void OnDeleteDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not BsonDocumentWrapper wrapper) return;

        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        var id = wrapper.GetRawValue("_id");
        bool confirm = await ConfirmWindow.Show(this, "确认删除", $"确定要删除 ID 为 '{id}' 的记录吗？\n(删除将在点击主界面[保存]按钮后正式生效)");

        if (confirm)
        {
            vm.MarkDocumentForDeletion(wrapper);
        }
    }

    #endregion

    #region 模板绑定、新建文档、Collection 管理

    private async void OnBindSchemaClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null || string.IsNullOrEmpty(vm.SelectedCollection)) return;

        // 自动加载当前表的模板原始文件（如果存在）
        var dialog = new SchemaEditorWindow { DataContext = new SchemaEditorViewModel(vm.CurrentBoundCsFilePath) };
        var result = await dialog.ShowDialog<SchemaEditorResult?>(this);

        if (result != null && !string.IsNullOrWhiteSpace(result.FilePath))
            vm.BindSchemaFile(result.FilePath);
    }

    private async void OnNewDocumentClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null || string.IsNullOrEmpty(vm.SelectedCollection) || vm.CurrentSchema == null) return;

        var newBson = new BsonDocument();

        // --- 智能确定新 ID 的类型与初始值 ---
        var firstDoc = System.Linq.Enumerable.FirstOrDefault(vm.Documents);
        var idVal = firstDoc?.Document["_id"];

        if (idVal == null || idVal.IsObjectId)
        {
            newBson["_id"] = ObjectId.NewObjectId();
        }
        else if (idVal.IsInt32 || idVal.IsInt64)
        {
            // 对于数字 ID，尝试查找当前最大值并 +1
            long maxId = 0;
            foreach (var doc in vm.Documents)
            {
                var curId = doc.GetRawValue("_id");
                if (curId.IsNumber) maxId = Math.Max(maxId, curId.AsInt64);
            }
            newBson["_id"] = (int)(maxId + 1);
        }
        else
        {
            // 字符串或其他类型，默认给个空
            newBson["_id"] = "";
        }

        var originalId = newBson["_id"]; // 记录初始生成的 ID 用于回滚

        var dialogVm = new DynamicPropertiesViewModel();
        // 提供实时查重逻辑
        dialogVm.GlobalIdDuplicateCheckFunc = (propName, bsonVal) => 
        {
            if (propName == "_id") return vm.IsIdDuplicate(bsonVal);
            return false;
        };

        dialogVm.LoadDocumentMetadata(newBson, vm.CurrentSchema, (updatedBson) =>
        {
            // 正常加入列表
            var newWrapper = new BsonDocumentWrapper(new BsonDocument(), w => vm.ForceSaveDocument(w));
            vm.Documents.Add(newWrapper);

            foreach (var kvp in updatedBson)
            {
                newWrapper.SetRawValueAndNotify(kvp.Key, kvp.Value);
            }

            newWrapper.RefreshAll();
            return Task.FromResult(true);
        });

        try
        {
            await new DynamicPropertiesWindow { DataContext = dialogVm }.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] OnNewDocumentClick dialog failed: {ex.Message}");
        }
    }

    private async void OnAddCollectionClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null || !vm.IsDatabaseLoaded) return;

        var dialog = new SchemaEditorWindow { DataContext = new SchemaEditorViewModel() };
        var result = await dialog.ShowDialog<SchemaEditorResult?>(this);

        if (result != null && !string.IsNullOrWhiteSpace(result.ClassName))
        {
            // 使用生成的类名作为表名，以及生成的 cs 文件作为模板
            vm.CreateCollection(result.ClassName, result.FilePath);
        }
    }

    private async void OnDeleteSpecificCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not string collectionName) return;
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        bool confirm = await ConfirmWindow.Show(this, "物理删除确认", $"❗ 警告：由于该操作不可撤销，确定要物理删除数据库中的表格 [{collectionName}] 及其所有数据吗？");
        if (confirm)
        {
            vm.DeleteSpecificCollectionCommand.Execute(collectionName);
        }
    }

    private void OnRenameCollectionConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var stackPanel = button.Parent as StackPanel;
        if (stackPanel == null) return;

        var textBox = stackPanel.Children.OfType<TextBox>().FirstOrDefault();
        if (textBox == null || string.IsNullOrWhiteSpace(textBox.Text)) return;

        var oldName = stackPanel.DataContext as string;
        var newName = textBox.Text.Trim();

        if (string.IsNullOrEmpty(oldName) || oldName == newName) return;

        var vm = DataContext as MainWindowViewModel;
        if (vm != null)
        {
            vm.RenameCollection(oldName, newName);
        }

        // 尝试关闭 Flyout
        var current = (Avalonia.Visual?)button;
        while (current != null)
        {
            if (current is Avalonia.Controls.Primitives.Popup popup)
            {
                popup.IsOpen = false;
                break;
            }
            if (current.GetType().Name.Contains("Popup"))
            {
                var isOpenProp = current.GetType().GetProperty("IsOpen");
                isOpenProp?.SetValue(current, false);
                break;
            }
            current = current.Parent as Avalonia.Visual;
        }
    }

    #endregion
}