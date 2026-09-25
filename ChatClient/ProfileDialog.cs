using ChatProtocol;

namespace ChatClient;

internal static class ProfileDialog
{
    public static void Show(UserProfile profile, bool isSelf, Action<string> onDirectMessage)
    {
        using var form = new Form
        {
            Text = isSelf ? "Hồ sơ của bạn" : $"Hồ sơ · {profile.Username}",
            ClientSize = new Size(390, 310),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = UiTheme.Canvas,
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 10F)
        };

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = UiTheme.Surface };
        var avatar = new Label
        {
            Text = profile.Username[..1].ToUpperInvariant(),
            Location = new Point(28, 28),
            Size = new Size(60, 60),
            BackColor = UiTheme.Primary,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 25F),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var name = new Label { Text = profile.Username, Location = new Point(104, 30), AutoSize = true, ForeColor = UiTheme.Text, Font = UiTheme.TitleFont };
        var status = new Label
        {
            Text = profile.IsOnline ? "●  Đang trực tuyến" : "●  Ngoại tuyến",
            Location = new Point(106, 67), AutoSize = true,
            ForeColor = profile.IsOnline ? UiTheme.Mint : UiTheme.Muted,
            Font = new Font("Segoe UI Semibold", 9F)
        };
        var divider = new Panel { Location = new Point(28, 112), Size = new Size(334, 1), BackColor = Color.FromArgb(55, 255, 255, 255) };
        var infoTitle = UiTheme.Label("THÀNH VIÊN TỪ", 8.5F, UiTheme.Muted, FontStyle.Bold);
        infoTitle.Location = new Point(28, 137);
        var joined = new Label
        {
            Text = profile.JoinedAt.LocalDateTime.ToString("dd/MM/yyyy"),
            Location = new Point(28, 158), AutoSize = true, ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI Semibold", 11F)
        };
        var note = UiTheme.Label(isSelf ? "Đây là hồ sơ hiển thị cho những người khác." : "Hồ sơ công khai trong ChatNet.", 9F);
        note.Location = new Point(28, 194);
        card.Controls.AddRange([avatar, name, status, divider, infoTitle, joined, note]);

        if (!isSelf)
        {
            var direct = new Button { Text = "Nhắn tin riêng", Location = new Point(28, 234), Size = new Size(170, 42) };
            UiTheme.StyleButton(direct, UiTheme.Primary, Color.White);
            direct.Click += (_, _) => { form.Close(); onDirectMessage(profile.Username); };
            card.Controls.Add(direct);
        }

        form.Controls.Add(card);
        form.ShowDialog();
    }
}
