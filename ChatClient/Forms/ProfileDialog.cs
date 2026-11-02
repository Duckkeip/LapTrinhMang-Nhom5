using ChatProtocol;
using System.Drawing.Drawing2D;

namespace ChatClient;

internal static class ProfileDialog
{
    public static void Show(NetworkClient network, UserProfile profile, bool isSelf, Action<string> onDirectMessage)
    {
        using var form = new Form
        {
            Text = isSelf ? "Hồ sơ của bạn" : $"Hồ sơ · {profile.DisplayName ?? profile.Username}",
            ClientSize = new Size(560, 480),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = UiTheme.Canvas,
            ForeColor = UiTheme.Text,
            Font = new Font("Times New Roman", 10F)
        };

        var mainTabControl = new TabControl 
        { 
            Dock = DockStyle.Fill, 
            Padding = new Point(12, 6),
            // Fix tab hiển thị trên dark theme
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(120, 36),
            Font = new Font("Times New Roman", 10F, FontStyle.Bold)
        };
        mainTabControl.DrawItem += (s, e) =>
        {
            var tab = mainTabControl.TabPages[e.Index];
            var brush = e.Index == mainTabControl.SelectedIndex 
                ? new SolidBrush(UiTheme.Primary) 
                : new SolidBrush(UiTheme.Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            var textBrush = new SolidBrush(UiTheme.Text);
            e.Graphics.DrawString(tab.Text, e.Font, textBrush, e.Bounds.X + 10, e.Bounds.Y + 5);
        };
        // ==================== TAB 1: THÔNG TIN ====================
        var tabInfo = new TabPage("Thông tin") { BackColor = UiTheme.Surface, Padding = new Padding(20) };

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = UiTheme.Surface };
        var avatar = new PictureBox
        {
            Location = new Point(28, 28),
            Size = new Size(60, 60),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = AvatarHelper.CreateRoundAvatar(profile.ShowAvatar ? profile.AvatarBase64 : null, profile.Username, 60)
        };
        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Username : profile.DisplayName;
        var name = new Label { Text = displayName, Location = new Point(104, 30), AutoSize = true, ForeColor = UiTheme.Text, Font = UiTheme.TitleFont };
        var status = new Label
        {
            Text = profile.ShowOnlineStatus ? GetStatusText(profile) : "●  Trạng thái ẩn",
            Location = new Point(106, 67), AutoSize = true,
            ForeColor = profile.Status == "busy" ? Color.FromArgb(251, 191, 36) : profile.IsOnline ? UiTheme.Mint : UiTheme.Muted,
            Font = new Font("Segoe UI Semibold", 9F)
        };
        var divider = new Panel { Location = new Point(28, 112), Size = new Size(334, 1), BackColor = Color.FromArgb(55, 255, 255, 255) };
        var infoTitle = UiTheme.Label("THÀNH VIÊN TỪ", 8.5F, UiTheme.Muted, FontStyle.Bold);
        infoTitle.Location = new Point(28, 137);
        var joined = new Label { Text = profile.JoinedAt.LocalDateTime.ToString("dd/MM/yyyy"), Location = new Point(28, 158), AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 11F) };
        var note = UiTheme.Label(isSelf ? "Đây là hồ sơ hiển thị cho những người khác." : "Hồ sơ công khai trong ChatNet.", 9F);
        note.Location = new Point(28, 194);
        var bio = new Label { Text = string.IsNullOrWhiteSpace(profile.Bio) ? "Chưa có tiểu sử." : profile.Bio, Location = new Point(28, 222), AutoSize = true, MaximumSize = new Size(334, 42), ForeColor = UiTheme.Text };
        card.Controls.AddRange([avatar, name, status, divider, infoTitle, joined, note, bio]);

        if (!isSelf)
        {
            var direct = new Button { Text = "Nhắn tin riêng", Location = new Point(28, 286), Size = new Size(170, 42) };
            UiTheme.StyleButton(direct, UiTheme.Primary, Color.White);
            direct.Click += (_, _) => { form.Close(); onDirectMessage(profile.Username); };
            card.Controls.Add(direct);
        }

        tabInfo.Controls.Add(card);

        // ==================== TAB 2: BẠN BÈ ====================
        var tabFriends = new TabPage("Bạn bè") { BackColor = UiTheme.Surface, Padding = new Padding(10) };

        if (isSelf)
        {
            var subTabControl = new TabControl { Dock = DockStyle.Fill };

            // Sub-tab 1: Danh sách bạn bè
            var subTabList = new TabPage("Danh sách");
            subTabList.Controls.Add(CreateFriendsPanel(network, profile.Friends ?? new(), onDirectMessage, form));
            subTabControl.TabPages.Add(subTabList);

            // Sub-tab 2: Lời mời đã nhận
            var subTabPendingReceived = new TabPage("Lời mời đã nhận");
            subTabPendingReceived.Controls.Add(CreatePendingReceivedPanel(network, profile.ReceivedFriendRequests  ?? new()));
            subTabControl.TabPages.Add(subTabPendingReceived);

            // Sub-tab 3: Đã gửi
            var subTabPendingSent = new TabPage("Đã gửi");
            subTabPendingSent.Controls.Add(CreatePendingSentPanel(network, profile.PendingFriendRequests  ?? new()));
            subTabControl.TabPages.Add(subTabPendingSent);

            tabFriends.Controls.Add(subTabControl);
            
        }
        else
        {
            // Profile người khác: chỉ xem danh sách bạn bè (nếu public)
            tabFriends.Controls.Add(CreateFriendsPanel(network, profile.Friends ?? new(), onDirectMessage, form));
        }
        
        mainTabControl.TabPages.Add(tabInfo);
        mainTabControl.TabPages.Add(tabFriends);

        form.Controls.Add(mainTabControl);
        // DEBUG: Kiểm tra số tab
        System.Diagnostics.Debug.WriteLine($"Tabs count: {mainTabControl.TabCount}");
        foreach (TabPage tp in mainTabControl.TabPages)
        {
            System.Diagnostics.Debug.WriteLine($"  Tab: {tp.Text}");
        }

        // Force select tab "Bạn bè" để test
        if (mainTabControl.TabPages["Bạn bè"] != null)
            mainTabControl.SelectedTab = mainTabControl.TabPages["Bạn bè"];

        form.ShowDialog();

        
    }

    // ============ PANEL: DANH SÁCH BẠN BÈ ============
    private static Panel CreateFriendsPanel(NetworkClient network, List<string> friends, Action<string> onDirectMessage, Form parentForm)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var listView = new ListView
        {
            Dock = DockStyle.Top,
            Height = 310,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false
        };
        listView.Columns.Add("Tên người dùng", 200);
        listView.Columns.Add("Trạng thái", 140);

        // Populate friends
        foreach (var friend in friends)
        {
            listView.Items.Add(new ListViewItem(new[] { friend, "Đang tải..." }) { Tag = friend });
        }

        var btnActionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };

        var btnMessage = new Button { Text = "Nhắn tin", Width = 95, Height = 32 };
        UiTheme.StyleButton(btnMessage, UiTheme.Primary, Color.White);
        btnMessage.Click += (_, _) =>
        {
            if (listView.SelectedItems.Count > 0)
            {
                var selectedUser = listView.SelectedItems[0].Tag?.ToString() ?? listView.SelectedItems[0].Text;
                parentForm.Close();
                onDirectMessage(selectedUser);
            }
        };

        var btnRemove = new Button { Text = "Xóa bạn", Width = 85, Height = 32 };
        UiTheme.StyleButton(btnRemove, Color.FromArgb(220, 38, 38), Color.White);
        btnRemove.Click += async (_, _) =>
        {
            if (listView.SelectedItems.Count == 0) return;
            var selectedUser = listView.SelectedItems[0].Tag?.ToString() ?? listView.SelectedItems[0].Text;
            var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa {selectedUser} khỏi danh sách bạn bè?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            btnRemove.Enabled = false;
            network.Send("remove-friend", new RemoveFriendRequest(selectedUser));
            
            // Optimistic UI update
            listView.SelectedItems[0].Remove();
            btnRemove.Enabled = true;
        };

        btnActionPanel.Controls.AddRange([btnMessage, btnRemove]);
        panel.Controls.Add(listView);
        panel.Controls.Add(btnActionPanel);
        return panel;
    }

    // ============ PANEL: LỜI MỜI ĐÃ NHẬN (Chấp nhận/Từ chối) ============
    private static Panel CreatePendingReceivedPanel(NetworkClient network, List<string> pendingReceived)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var listView = new ListView
        {
            Dock = DockStyle.Top,
            Height = 310,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false
        };
        listView.Columns.Add("Tên người dùng", 200);
        listView.Columns.Add("Thời gian", 140);

        foreach (var user in pendingReceived)
        {
            listView.Items.Add(new ListViewItem(new[] { user, "Vừa xong" }) { Tag = user });
        }

        var btnActionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };

        var btnAccept = new Button { Text = "Chấp nhận", Width = 95, Height = 32 };
        UiTheme.StyleButton(btnAccept, UiTheme.Mint, Color.Black);
        btnAccept.Click += (_, _) =>
        {
            if (listView.SelectedItems.Count == 0) return;
            var selectedUser = listView.SelectedItems[0].Tag?.ToString() ?? listView.SelectedItems[0].Text;
            btnAccept.Enabled = false;
            network.Send("friend-respond", new RespondFriendRequest(selectedUser, true));
            listView.SelectedItems[0].Remove();
            btnAccept.Enabled = true;
        };

        var btnDecline = new Button { Text = "Từ chối", Width = 85, Height = 32 };
        UiTheme.StyleButton(btnDecline, Color.FromArgb(220, 38, 38), Color.White);
        btnDecline.Click += (_, _) =>
        {
            if (listView.SelectedItems.Count == 0) return;
            var selectedUser = listView.SelectedItems[0].Tag?.ToString() ?? listView.SelectedItems[0].Text;
            btnDecline.Enabled = false;
            network.Send("friend-respond", new RespondFriendRequest(selectedUser, false));
            listView.SelectedItems[0].Remove();
            btnDecline.Enabled = true;
        };

        btnActionPanel.Controls.AddRange([btnAccept, btnDecline]);
        panel.Controls.Add(listView);
        panel.Controls.Add(btnActionPanel);
        return panel;
    }

    // ============ PANEL: ĐÃ GỬI (Hủy lời mời) ============
    private static Panel CreatePendingSentPanel(NetworkClient network, List<string> pendingSent)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var listView = new ListView
        {
            Dock = DockStyle.Top,
            Height = 310,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false
        };
        listView.Columns.Add("Tên người dùng", 200);
        listView.Columns.Add("Thời gian", 140);

        foreach (var user in pendingSent)
        {
            listView.Items.Add(new ListViewItem(new[] { user, "Đang chờ" }) { Tag = user });
        }

        var btnActionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };

        var btnCancel = new Button { Text = "Hủy lời mời", Width = 100, Height = 32 };
        UiTheme.StyleButton(btnCancel, UiTheme.Muted, Color.White);
        btnCancel.Click += (_, _) =>
        {
            if (listView.SelectedItems.Count == 0) return;
            var selectedUser = listView.SelectedItems[0].Tag?.ToString() ?? listView.SelectedItems[0].Text;
            btnCancel.Enabled = false;
            // Hủy lời mời = respond với accept=false (từ chối từ phía người gửi)
            network.Send("friend-respond", new RespondFriendRequest(selectedUser, false));
            listView.SelectedItems[0].Remove();
            btnCancel.Enabled = true;
        };

        btnActionPanel.Controls.Add(btnCancel);
        panel.Controls.Add(listView);
        panel.Controls.Add(btnActionPanel);
        return panel;
    }
    private static void RequestRefresh(NetworkClient network, string username, Action onRefreshRequested)
    {
        // Gửi request profile, server sẽ push về -> UserMainForm xử lý -> gọi onRefreshRequested
        network.Send("profile", new ProfileRequest(username));
        onRefreshRequested?.Invoke();
    }
    private static string GetStatusText(UserProfile profile) => profile.Status switch
    {
        "busy" => "●  Đang bận",
        "hidden" => "●  Ẩn trạng thái",
        _ => profile.IsOnline ? "●  Đang trực tuyến" : "●  Ngoại tuyến"
    };
}