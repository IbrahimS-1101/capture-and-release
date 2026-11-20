using System.Diagnostics;
namespace GameplayRecorder;
internal sealed class AboutForm : Form
{
    internal const string CoffeeUrl="https://buymeacoffee.com/isamir";
    internal const string XUrl="https://x.com/isamirDM";
    internal const string SupportText="Capture & Release is free to use. If you find it useful, sharing it anywhere or buying me a coffee would be appreciated. A mention on X is especially welcome. Support is entirely optional.";
    internal const string Credits = """
        Capture & Release · 0.4.0 beta 1
        Lightweight Windows screen/window recording and video tools.
        Record application audio. Trim, crop, filter and adjust sound.

        Created by Ibrahim Samir
        Copyright (c) 2026 Ibrahim Samir
        Developed with assistance from OpenAI Codex.

        Application source: GPL-3.0-only
        Free to use, study, modify and redistribute under that license.
        Provided without warranty. See the included LICENSE.

        FFmpeg: recording, encoding, trimming, cropping and audio mixing.
        CRT rendering: our shader, adapted for FFmpeg/libplacebo + Vulkan.
        Setup obtains FFmpeg 8.0.1 from Gyan Doshi's builds.
        FFmpeg and its libraries retain their own licenses and credits.
        The current GitHub download obtains FFmpeg separately.
        See THIRD_PARTY_NOTICES.md and app/tools notices.

        Windows application audio: our C++ WASAPI process-loopback helper.
        Desktop interface and playback: Microsoft .NET Windows Forms / WPF.

        No microphone capture, streaming or automatic uploads.
        Screen mode includes other visible windows and notifications.
        Window capture support varies; Chrome can produce black video.
        Fullscreen display changes may interrupt recording.
        """;
    public AboutForm()
    {
        Text="About Capture & Release";Font=new Font("Segoe UI",10);
        BackColor=Color.FromArgb(247,249,251);StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(640,720);MinimumSize=new Size(580,580);
        ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;
        Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),RowCount=3,ColumnCount=1};
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,128));layout.RowStyles.Add(new(SizeType.Absolute,45));
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
        var support=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(0,10,0,0)};support.RowStyles.Add(new(SizeType.Percent,100));support.RowStyles.Add(new(SizeType.Absolute,32));
        support.Controls.Add(new Label{Text=SupportText,Dock=DockStyle.Fill,ForeColor=Color.FromArgb(76,86,99)},0,0);
        var supportLinks=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        var coffee=new LinkLabel{Name="coffeeLink",Text="Buy me a coffee",AutoSize=true,Margin=new Padding(0,4,24,0)};
        var x=new LinkLabel{Name="xLink",Text="Find / mention me on X",AutoSize=true,Margin=new Padding(0,4,0,0)};
        coffee.Links[0].LinkData=CoffeeUrl;x.Links[0].LinkData=XUrl;
        coffee.LinkClicked+=(_,e)=>OpenLink((string)e.Link!.LinkData!);x.LinkClicked+=(_,e)=>OpenLink((string)e.Link!.LinkData!);supportLinks.Controls.AddRange(new Control[]{coffee,x});support.Controls.Add(supportLinks,0,1);
        layout.Controls.Add(text,0,0);layout.Controls.Add(support,0,1);layout.Controls.Add(buttons,0,2);Controls.Add(layout);
        AcceptButton=close;CancelButton=close;
    }
    void OpenLink(string url)
    {
        try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
        catch(Exception e){MessageBox.Show(this,"Could not open the browser.\n"+e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }
}
