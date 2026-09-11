// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Drawing;
using System.Windows.Forms;

namespace CrestronHomeNUnit.Runner;

internal sealed class ProcessorCredentialsDialog : Form
	{
	private readonly TextBox _user = new () { Width = 270 };
	private readonly TextBox _password = new () { Width = 270, UseSystemPasswordChar = true };
	public string User => _user.Text.Trim ();
	public string Password => _password.Text;
	public ProcessorCredentialsDialog (string host, string user)
		{
		Text = "Sign in to " + host;
		Font = new Font ("Segoe UI", 10);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size (430, 185);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		MaximizeBox = false;
		_user.Text = user;
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding (12), ColumnCount = 2, RowCount = 4 };
		var title = new Label { Text = "Processor SFTP credentials", AutoSize = true };
		layout.Controls.Add (title, 0, 0);
		layout.SetColumnSpan (title, 2);
		layout.Controls.Add (new Label { Text = "Username", AutoSize = true }, 0, 1);
		layout.Controls.Add (_user, 1, 1);
		layout.Controls.Add (new Label { Text = "Password", AutoSize = true }, 0, 2);
		layout.Controls.Add (_password, 1, 2);
		var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
		var connect = new Button { Text = "Connect", AutoSize = true, DialogResult = DialogResult.OK };
		var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
		buttons.Controls.AddRange ([connect, cancel]);
		layout.Controls.Add (buttons, 1, 3);
		Controls.Add (layout);
		AcceptButton = connect;
		CancelButton = cancel;
		Shown += (_, _) => (user.Length == 0 ? _user : _password).Focus ();
		}
	}