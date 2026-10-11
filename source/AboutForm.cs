using System.Diagnostics;
namespace GameplayRecorder;
internal sealed class AboutForm : Form
{
    internal const string Credits = """
        Capture & Release · 0.3.0 beta
        Lightweight Windows screen and window recording
        with selected application audio.

        Created by Ibrahim Samir
        Copyright (c) 2026 Ibrahim Samir
        Developed with assistance from OpenAI Codex.

        Application source: GPL-3.0-only
        Free to use, study, modify and redistribute under that license.
        Provided without warranty. See the included LICENSE.

        FFmpeg: recording, video encoding and MP4 processing.
        Setup downloads FFmpeg 8.0.1 from Gyan Doshi's builds.
        FFmpeg and its libraries retain their own licenses and credits.
        FFmpeg binaries are not included in our GitHub download.
        See THIRD_PARTY_NOTICES.md and the downloaded tools' notices.

        Windows application audio: our C++ WASAPI process-loopback helper.
        Desktop interface: Microsoft .NET Windows Forms.

        No microphone capture, streaming or automatic uploads.
        Screen mode includes other visible windows and notifications.
        Window capture support varies; Chrome can produce black video.
        Fullscreen display changes may interrupt recording.
        """;
    public AboutForm()
    {
        Text="About Capture & Release";Font=new Font("Segoe UI",10);
        BackColor=Color.FromArgb(247,249,251);StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(620,640);MinimumSize=new Size(580,520);
        ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;
        Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),RowCount=2,ColumnCount=1};
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,45));
        var text=new TextBox{Text=Credits.Replace("\n",Environment.NewLine),Multiline=true,ReadOnly=true,
            ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,
            BackColor=BackColor,TabStop=false};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)};
        var project=new Button{Text="GitHub project",AutoSize=true};
        project.Click+=(_,_)=>OpenLink("https://github.com/IbrahimS-1101/capture-and-release");
        var ffmpeg=new Button{Text="FFmpeg credits",AutoSize=true};
        ffmpeg.Click+=(_,_)=>OpenLink("https://ffmpeg.org/legal.html");
        var close=new Button{Text="Close",AutoSize=true,DialogResult=DialogResult.OK};
        buttons.Controls.AddRange(new Control[]{project,ffmpeg,close});
        layout.Controls.Add(text,0,0);layout.Controls.Add(buttons,0,1);Controls.Add(layout);
        AcceptButton=close;CancelButton=close;
    }
    void OpenLink(string url)
    {
        try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
        catch(Exception e){MessageBox.Show(this,"Could not open the browser.\n"+e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }
}
