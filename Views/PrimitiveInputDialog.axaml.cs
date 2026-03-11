using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LiteDBEditor.Views;

/// <summary>
/// 根据 typeName 对 TextBox 进行字符级输入过滤的轻量弹窗。
/// - Int32 / Int64：只允许数字和负号（负号只能在最前）
/// - Double：只允许数字、小数点（最多 1 个）和负号
/// - Boolean：显示复选框
/// - String 及其他：不限制
/// </summary>
public partial class PrimitiveInputDialog : Window
{
    #region 字段

    private readonly string _typeName;
    private TextBox? _inputBox;
    private CheckBox? _boolBox;

    #endregion

    #region 初始化

    public PrimitiveInputDialog()
    {
        InitializeComponent();
        _typeName = "String";
    }

    public PrimitiveInputDialog(string prompt, string typeName, string defaultVal = "")
    {
        InitializeComponent();
        _typeName = typeName;

        _inputBox = this.FindControl<TextBox>("InputBox");
        _boolBox = this.FindControl<CheckBox>("BoolBox");
        var promptText = this.FindControl<TextBlock>("PromptText");
        if (promptText != null) promptText.Text = prompt;

        if (typeName == "Boolean")
        {
            // Boolean 用复选框
            if (_inputBox != null) _inputBox.IsVisible = false;
            if (_boolBox != null)
            {
                _boolBox.IsVisible = true;
                _boolBox.IsChecked = defaultVal?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            }
        }
        else
        {
            // 数值和字符串都用 TextBox，但数值会附加键盘过滤
            if (_inputBox != null)
            {
                _inputBox.Text = defaultVal;
                // Tunnel 拦截优先于 TextBox 内建处理
                _inputBox.AddHandler(
                    InputElement.TextInputEvent,
                    OnTextInput,
                    RoutingStrategies.Tunnel);
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    #endregion

    #region 焦点获取

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_inputBox?.IsVisible == true)
        {
            _inputBox.Focus();
            _inputBox.SelectAll();
        }
        else if (_boolBox?.IsVisible == true)
        {
            _boolBox.Focus();
        }
    }

    #endregion

    #region 输入过滤

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text == null || _typeName is "String" or "DateTime" or "ObjectId") return;

        foreach (char c in e.Text)
        {
            bool allowed = _typeName switch
            {
                // 整数：数字 + 仅首位负号
                "Int32" or "Int64" =>
                    char.IsDigit(c)
                    || (c == '-' && (_inputBox?.Text?.Length == 0
                                     || _inputBox?.SelectionStart == 0)),

                // 浮点：数字 + 仅首位负号 + 最多 1 个小数点
                "Double" or "Float" or "Single" =>
                    char.IsDigit(c)
                    || (c == '-' && (_inputBox?.Text?.Length == 0
                                     || _inputBox?.SelectionStart == 0))
                    || (c == '.' && _inputBox?.Text?.Contains('.') != true),

                _ => true
            };

            if (!allowed)
            {
                // 拦截整个本次 TextInput 事件，字符不会写入 TextBox
                e.Handled = true;
                return;
            }
        }
    }

    #endregion

    #region 确认 / 取消

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (_typeName == "Boolean")
        {
            Close(_boolBox?.IsChecked == true ? "True" : "False");
        }
        else
        {
            var text = _inputBox?.Text ?? "";
            // 数值类型输入框为空时，用默认值 0 代替空字符串写入集合
            if (string.IsNullOrWhiteSpace(text) && _typeName is "Int32" or "Int64" or "Double" or "Float" or "Single")
                text = "0";
            Close(text);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    #endregion
}
