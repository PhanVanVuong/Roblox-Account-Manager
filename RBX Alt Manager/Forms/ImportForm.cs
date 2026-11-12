using RBX_Alt_Manager.Forms;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RBX_Alt_Manager
{
    public partial class ImportForm : Form
    {
        private bool ImportCombo;

        public void SetImportMode(bool combo)
        {
            if (ImportCombo != combo) Accounts.Clear();
            ImportCombo = combo;
            Text = combo ? "Import User:Pass:Cookie" : "Import Cookies";
            label1.Text = combo
                ? "One user:pass:cookie per line. Authentication uses only the cookie."
                : "Import Accounts by ROBLOSECURITY, separated by new lines";
        }

        internal static bool TryParseCombo(string line, out string username, out string password, out string cookie)
        {
            username = password = cookie = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            int first = line.IndexOf(':');
            int second = first < 0 ? -1 : line.IndexOf(':', first + 1);
            if (first <= 0 || second < 0) return false;
            username = line.Substring(0, first).Trim();
            password = line.Substring(first + 1, second - first - 1);
            cookie = line.Substring(second + 1).Trim();
            return username.Length > 0 && cookie.Length > 0;
        }

        public ImportForm()
        {
            AccountManager.SetDarkBar(Handle);

            InitializeComponent();
            this.Rescale();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeEditor.FormsBackground;
            ForeColor = ThemeEditor.FormsForeground;

            foreach (Control control in this.Controls)
            {
                if (control is Button || control is CheckBox)
                {
                    if (control is Button)
                    {
                        Button b = control as Button;
                        b.FlatStyle = ThemeEditor.ButtonStyle;
                        b.FlatAppearance.BorderColor = ThemeEditor.ButtonsBorder;
                    }

                    if (!(control is CheckBox)) control.BackColor = ThemeEditor.ButtonsBackground;
                    control.ForeColor = ThemeEditor.ButtonsForeground;
                }
                else if (control is TextBox || control is RichTextBox)
                {
                    if (control is Classes.BorderedTextBox)
                    {
                        Classes.BorderedTextBox b = control as Classes.BorderedTextBox;
                        b.BorderColor = ThemeEditor.TextBoxesBorder;
                    }

                    if (control is Classes.BorderedRichTextBox)
                    {
                        Classes.BorderedRichTextBox b = control as Classes.BorderedRichTextBox;
                        b.BorderColor = ThemeEditor.TextBoxesBorder;
                    }

                    control.BackColor = ThemeEditor.TextBoxesBackground;
                    control.ForeColor = ThemeEditor.TextBoxesForeground;
                }
                else if (control is Label)
                {
                    control.BackColor = ThemeEditor.LabelTransparent ? Color.Transparent : ThemeEditor.LabelBackground;
                    control.ForeColor = ThemeEditor.LabelForeground;
                }
                else if (control is ListBox)
                {
                    control.BackColor = ThemeEditor.ButtonsBackground;
                    control.ForeColor = ThemeEditor.ButtonsForeground;
                }
            }
        }

        private void ImportForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            string[] Lines = Accounts.Text.Split('\n');
            var Results = new List<string>();
            for (int i = 0; i < Lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(Lines[i])) continue;
                string Username = null, Password = "", Cookie = Lines[i].Trim();
                if (ImportCombo && !TryParseCombo(Lines[i], out Username, out Password, out Cookie))
                {
                    Results.Add($"Line {i + 1}: invalid user:pass:cookie format.");
                    continue;
                }
                try
                {
                    Account NewAccount = AccountManager.AddAccount(Cookie, Password, ExpectedUsername: Username);
                    Results.Add(NewAccount != null
                        ? $"Line {i + 1}: saved {NewAccount.Username}."
                        : $"Line {i + 1}: invalid cookie or username does not match the cookie.");
                }
                catch (Exception)
                {
                    Results.Add($"Line {i + 1}: import failed. Check your connection and try again.");
                }
            }
            Accounts.Text = string.Join(Environment.NewLine, Results);
        }
    }
}
