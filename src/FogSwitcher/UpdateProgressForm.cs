namespace FogSwitcher;

internal sealed class UpdateProgressForm : Form
{
    public UpdateProgressForm()
    {
        Text = "Installing update";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        ClientSize = new Size(380, 112);

        var messageLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(16, 16, 16, 0),
            Text = "Downloading the latest Fog Switcher update.\nThe app will restart automatically when ready."
        };

        var progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 16,
            Margin = Padding.Empty,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25
        };

        var progressHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 16)
        };
        progressHost.Controls.Add(progressBar);

        Controls.Add(progressHost);
        Controls.Add(messageLabel);
    }
}
