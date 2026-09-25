namespace ChatClient;

/// <summary>Hop thoai nhap 1 dong text don gian, dung thay cho Microsoft.VisualBasic.InputBox
/// de khong phai them package phu.</summary>
public static class PromptInputBox
{
    public static string? Show(string title, string label)
    {
        using var form = new Form
        {
            Width = 390,
            Height = 190,
            Text = title,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = UiTheme.Canvas,
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 10F)
        };

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(24), BackColor = UiTheme.Surface };
        var lbl = new Label { Text = label, Left = 24, Top = 26, Width = 320, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 11F) };
        var txt = new TextBox { Left = 24, Top = 58, Width = 320, Height = 30 };
        UiTheme.StyleInput(txt);
        var btnOk = new Button { Text = "Xác nhận", Left = 164, Top = 108, Width = 98, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Hủy", Left = 270, Top = 108, Width = 74, DialogResult = DialogResult.Cancel };
        UiTheme.StyleButton(btnOk, UiTheme.Primary, Color.White);
        UiTheme.StyleButton(btnCancel, UiTheme.SurfaceRaised, UiTheme.Text);

        card.Controls.Add(lbl);
        card.Controls.Add(txt);
        card.Controls.Add(btnOk);
        card.Controls.Add(btnCancel);
        form.Controls.Add(card);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }
}
