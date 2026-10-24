using System.Windows;

namespace PromptPolisher.App.Views;

public partial class ManualResultWindow : Window
{
    public ManualResultWindow(string renderedPrompt, string providerName)
    {
        InitializeComponent();
        HeaderText.Text = $"提供方：{providerName}。应用已生成最终 Prompt；请复制到网页版，完成后把结果粘贴回来。";
        PromptBox.Text = renderedPrompt;
    }

    public string? ResultText { get; private set; }

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PromptBox.Text);
            HeaderText.Text = "已复制。请到网页版粘贴并获取结果，然后回到这里粘贴结果。";
        }
        catch
        {
            MessageBox.Show("复制失败：剪贴板被占用，请手动选择复制。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var result = ResultBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            MessageBox.Show("请先粘贴网页版返回的结果。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResultText = result;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
