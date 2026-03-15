using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.Services;

namespace LiteDBEditor.ViewModels;

public partial class DynamicPropertiesViewModel : ViewModelBase
{
    #region 弹窗状态与集合
    
    public event Action<DynamicPropertyItemViewModel>? RequestScrollToError;

    [ObservableProperty]
    private string _title = LanguageService.GetString("L_EditDocument");

    [ObservableProperty]
    private ObservableCollection<DynamicPropertyItemViewModel> _properties = new();

    [ObservableProperty]
    private string? _windowErrorMessage;

    [ObservableProperty]
    private string? _contextPath;

    private BsonDocument? _targetBsonDocument;
    private Func<BsonDocument, Task<bool>>? _onSaveAsync;
    private SchemaData? _schemaData;

    /// <summary>
    /// 注入的全局查重回调
    /// </summary>
    public Func<string, BsonValue, bool>? GlobalIdDuplicateCheckFunc { get; set; }

    #endregion

    #region 初始化与生成动态项目

    /// <summary>
    /// 初始化顶层 Document 编辑器
    /// </summary>
    /// <param name="document">要编辑的字典</param>
    /// <param name="schemaData">元数据定义</param>
    /// <param name="onSaveAsync">保存回调，返回是否允许保存（用于逻辑校验校验，如 ID 查重）</param>
    /// <param name="contextPath">外部传入的路径描述（可选）</param>
    public void LoadDocumentMetadata(BsonDocument document, SchemaData schemaData, Func<BsonDocument, Task<bool>> onSaveAsync, string? contextPath = null)
    {
        _targetBsonDocument = document;
        _schemaData = schemaData;
        _onSaveAsync = onSaveAsync;
        ContextPath = contextPath;

        UpdateTitle();
        
        // 订阅语言变更以更新标题
        LanguageService.LanguageChanged += UpdateTitle;

        Properties.Clear();

        // 依据 Schema 定义构建第一层属性项
        foreach (var propertySchema in schemaData.Properties)
        {
            var itemVm = new DynamicPropertyItemViewModel();
            itemVm.IdDuplicateCheckFunc = GlobalIdDuplicateCheckFunc;
            // 在这一步将会把 document 注入到 ViewModel 里进行监控管理
            itemVm.InitializeWithDocument(_targetBsonDocument, propertySchema.Name, propertySchema);
            Properties.Add(itemVm);
        }

        // 把没有在 Schema 里面定义出来的遗留字段也附加上，类型当做 String
        foreach (var key in _targetBsonDocument.Keys)
        {
            if (!schemaData.Properties.Exists(p => p.Name == key))
            {
                var fallbackSchema = new SchemaProperty
                {
                    Name = key,
                    DisplayName = key,
                    TypeName = "String"
                };
                var itemVm = new DynamicPropertyItemViewModel();
                itemVm.InitializeWithDocument(_targetBsonDocument, key, fallbackSchema);
                Properties.Add(itemVm);
            }
        }
    }

    #endregion

    #region 外部访问器

    /// <summary>
    /// 返回当前正在编辑的 BsonDocument，供洗窗后台代码使用。
    /// </summary>
    public BsonDocument? GetTargetDocument() => _targetBsonDocument;

    #endregion

    #region 保存动作

    /// <summary>
    /// 执行保存逻辑，并返回外部回调的执行结果。
    /// </summary>
    public async Task<bool> ExecuteSaveAsync()
    {
        WindowErrorMessage = null;

        var errorNames = new System.Collections.Generic.List<string>();
        DynamicPropertyItemViewModel? firstErrorVm = null;

        foreach (var prop in Properties)
        {
            var vm = prop.CollectAllErrors(errorNames);
            if (firstErrorVm == null && vm != null)
            {
                firstErrorVm = vm;
            }
        }

        if (errorNames.Count > 0)
        {
            WindowErrorMessage = $"{LanguageService.GetString("L_ValidationErrorPrefix")}{string.Join(", ", errorNames)}";
            
            if (firstErrorVm != null)
            {
                RequestScrollToError?.Invoke(firstErrorVm);
            }
            return false;
        }

        if (_targetBsonDocument != null && _onSaveAsync != null)
        {
            return await _onSaveAsync.Invoke(_targetBsonDocument);
        }
        return true;
    }

    #endregion

    private void UpdateTitle()
    {
        if (_schemaData == null)
        {
            Title = LanguageService.GetString("L_EditDocument");
            return;
        }

        Title = string.IsNullOrEmpty(ContextPath)
            ? $"{LanguageService.GetString("L_EditDocument")} - {_schemaData.TargetName}"
            : $"{LanguageService.GetString("L_EditData")}: {ContextPath}";
    }

    public void ClearErrors()
    {
        WindowErrorMessage = null;
        foreach (var prop in Properties)
        {
            prop.ErrorMessage = null;
        }
    }
}
