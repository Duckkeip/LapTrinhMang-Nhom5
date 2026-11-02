namespace ChatClient;

internal static class PasswordChangeDialog
{
    public static string? Show(IWin32Window owner)
    {
        using var form = new Form
        {
            Text = "Đổi mật khẩu",
            ClientSize = new Size(420, 270),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = UiTheme.Canvas,
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 10F)
        };

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(24), BackColor = UiTheme.Surface };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 3; index++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var next = CreatePasswordBox();
        var confirm = CreatePasswordBox();
        AddField(layout, 0, "Mật khẩu mới", next);
        AddField(layout, 1, "Nhập lại mật khẩu", confirm);
        layout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        var cancel = new Button { Text = "Hủy", Width = 80, Height = 36, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Cập nhật", Width = 100, Height = 36, DialogResult = DialogResult.OK };
        UiTheme.StyleButton(cancel, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(save, UiTheme.Primary, Color.White);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);
        card.Controls.Add(layout);
        form.Controls.Add(card);
        form.AcceptButton = save;
        form.CancelButton = cancel;

        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        if (string.IsNullOrWhiteSpace(next.Text)) return null;
        if (next.Text != confirm.Text)
        {
            MessageBox.Show(owner, "Mật khẩu nhập lại không khớp.", "Đổi mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return next.Text;
    }

    private static TextBox CreatePasswordBox()
    {
        var box = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(0, 4, 0, 4) };
        UiTheme.StyleInput(box);
        return box;
    }

    private static void AddField(TableLayoutPanel layout, int row, string label, TextBox input)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        layout.Controls.Add(input, 1, row);
    }
}
