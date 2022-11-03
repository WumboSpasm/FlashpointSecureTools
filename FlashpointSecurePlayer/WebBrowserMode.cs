﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Windows.Forms;

using static FlashpointSecurePlayer.Shared;
using static FlashpointSecurePlayer.Shared.Exceptions;

using SHDocVw;

namespace FlashpointSecurePlayer {
    public partial class WebBrowserMode : Form {
        private bool resizable = true;

        public bool Resizable {
            get {
                return resizable;
            }

            set {
                resizable = value;

                if (resizable) {
                    FormBorderStyle = FormBorderStyle.Sizable;

                    // only set to true if it isn't already to avoid bug exiting fullscreen
                    if (!MaximizeBox) {
                        MaximizeBox = true;
                    }
                } else {
                    if (fullscreen) {
                        FormBorderStyle = FormBorderStyle.None;
                        return;
                    }

                    FormBorderStyle = FormBorderStyle.FixedSingle;

                    if (MaximizeBox) {
                        MaximizeBox = false;
                    }
                }
            }
        }

        private HookProc lowLevelMouseProc = null;
        private IntPtr mouseHook = IntPtr.Zero;

        private IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0) {
                if (Fullscreen) {
                    if (wParam.ToInt32() == WM_MOUSEMOVE) {
                        // this is checked in LowLevelMouseProc because
                        // otherwise plugins such as Viscape which
                        // create their own window can steal the
                        // mouse move event
                        Point toolBarToolStripMousePosition = toolBarToolStrip.PointToClient(Control.MousePosition);

                        if (toolBarToolStrip.Visible) {
                            if (!toolBarToolStrip.ClientRectangle.Contains(toolBarToolStripMousePosition)) {
                                toolBarToolStrip.Visible = false;
                            }
                        } else {
                            if (toolBarToolStripMousePosition.Y == 0
                                && toolBarToolStrip.ClientRectangle.Contains(toolBarToolStripMousePosition)) {
                                toolBarToolStrip.Visible = true;
                            }
                        }
                    }
                }
            }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private const int FULLSCREEN_EXIT_LABEL_TIMER_TIME = 2500;

        private System.Windows.Forms.Timer exitFullscreenLabelTimer = null;

        private bool ExitFullscreenLabelTimer {
            get {
                return exitFullscreenLabelTimer != null;
            }

            set {
                if (exitFullscreenLabelTimer != null) {
                    exitFullscreenLabelTimer.Stop();
                    exitFullscreenLabelTimer.Tick -= exitFullscreenLabelTimer_Tick;
                    exitFullscreenLabelTimer.Dispose();
                    exitFullscreenLabelTimer = null;
                }

                exitFullscreenLabel.Visible = value;

                if (exitFullscreenLabel.Visible) {
                    exitFullscreenLabelTimer = new System.Windows.Forms.Timer();
                    exitFullscreenLabelTimer.Interval = FULLSCREEN_EXIT_LABEL_TIMER_TIME;
                    exitFullscreenLabelTimer.Tick += exitFullscreenLabelTimer_Tick;
                    exitFullscreenLabelTimer.Start();
                }
            }
        }

        private bool fullscreen = false;
        private bool fullscreenResizable = true;
        private FormWindowState fullscreenWindowState = FormWindowState.Maximized;

        private Point closableWebBrowserAnchorLocation;
        private Size closableWebBrowserAnchorSize;

        public bool Fullscreen {
            get {
                return fullscreen;
            }

            set {
                fullscreen = value;

                if (fullscreen) {
                    // make Strips invisible so the Closable Web Browser can fill their space
                    fullscreenButton.Checked = true;
                    toolBarToolStrip.Visible = false;
                    statusBarStatusStrip.Visible = false;

                    // switch the Closable Web Browser to Docked
                    closableWebBrowserAnchorLocation = closableWebBrowser.Location;
                    closableWebBrowserAnchorSize = closableWebBrowser.Size;
                    closableWebBrowser.Dock = DockStyle.Fill;

                    // get the original properties before modifying them
                    fullscreenResizable = Resizable;
                    fullscreenWindowState = WindowState;

                    // need to do this first to have an effect if starting maximized
                    WindowState = FormWindowState.Normal;
                    // disable resizing
                    Resizable = false;
                    // enter fullscreen
                    WindowState = FormWindowState.Maximized;

                    // now that we've changed states, bring the window to the front
                    BringToFront();

                    if (mouseHook == IntPtr.Zero && lowLevelMouseProc != null) {
                        mouseHook = SetWindowsHookEx(HookType.WH_MOUSE_LL, lowLevelMouseProc, IntPtr.Zero, 0);
                    }

                    ExitFullscreenLabelTimer = true;
                } else {
                    ExitFullscreenLabelTimer = false;

                    if (mouseHook != IntPtr.Zero) {
                        if (UnhookWindowsHookEx(mouseHook)) {
                            mouseHook = IntPtr.Zero;
                        }
                    }

                    // need to do this first to reset the window to its former size
                    Resizable = fullscreenResizable;
                    // exit fullscreen
                    WindowState = FormWindowState.Normal;
                    // reset window state to the original one before changing it
                    WindowState = fullscreenWindowState;

                    // now that we've changed states, bring the window to the front
                    BringToFront();

                    // make these visible again so the browser can anchor to them
                    fullscreenButton.Checked = false;
                    toolBarToolStrip.Visible = true;
                    statusBarStatusStrip.Visible = true;

                    // switch the Closable Web Browser to Anchored
                    closableWebBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                    closableWebBrowser.Location = closableWebBrowserAnchorLocation;
                    closableWebBrowser.Size = closableWebBrowserAnchorSize;
                }
            }
        }

        private class EndEllipsisTextRenderer : ToolStripProfessionalRenderer {
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
                if (e.Item is ToolStripStatusLabel) {
                    TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, e.TextFormat | TextFormatFlags.EndEllipsis);
                    return;
                }

                base.OnRenderItemText(e);
            }
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        private class MessageFilter : IMessageFilter {
            private readonly EventHandler Back;
            private readonly EventHandler Forward;

            public MessageFilter(EventHandler Back, EventHandler Forward) {
                this.Back = Back;
                this.Forward = Forward;
            }

            protected virtual void OnBack(EventArgs e) {
                EventHandler eventHandler = Back;

                if (eventHandler == null) {
                    return;
                }

                eventHandler(this, e);
            }

            protected virtual void OnForward(EventArgs e) {
                EventHandler eventHandler = Forward;

                if (eventHandler == null) {
                    return;
                }

                eventHandler(this, e);
            }

            [SecurityPermission(SecurityAction.Demand)]
            public bool PreFilterMessage(ref Message m) {
                if (m.Msg == WM_XBUTTONUP) {
                    int wParam = m.WParam.ToInt32();

                    if ((wParam & MK_XBUTTON1) == MK_XBUTTON1) {
                        OnBack(EventArgs.Empty);
                        return true;
                    }

                    if ((wParam & MK_XBUTTON2) == MK_XBUTTON2) {
                        OnForward(EventArgs.Empty);
                        return true;
                    }
                }
                return false;
            }
        }

        private MessageFilter messageFilter = null;

        private class TitleChangedEventArgs : EventArgs {
            public string Text { get; set; } = null;

            public TitleChangedEventArgs(string text) {
                Text = text;
            }
        }

        private class WebBrowserModeTitle {
            private readonly EventHandler<TitleChangedEventArgs> TitleChanged;

            private readonly string applicationTitle = "Flashpoint Secure Player";
            private string documentTitle = null;
            private int progress = -1;

            public WebBrowserModeTitle(EventHandler<TitleChangedEventArgs> TitleChanged) {
                this.TitleChanged = TitleChanged;
                this.applicationTitle += " " + typeof(WebBrowserModeTitle).Assembly.GetName().Version;

                Show();
            }

            protected virtual void OnTitleChanged(TitleChangedEventArgs e) {
                EventHandler<TitleChangedEventArgs> eventHandler = TitleChanged;

                if (eventHandler == null) {
                    return;
                }

                eventHandler(this, e);
            }

            private void Show() {
                StringBuilder text = new StringBuilder();

                if (!String.IsNullOrEmpty(documentTitle)) {
                    text.Append(documentTitle);
                    text.Append(" - ");
                }

                text.Append(applicationTitle);

                if (progress != -1) {
                    text.Append(" [");
                    text.Append(progress);
                    text.Append("%]");
                }

                OnTitleChanged(new TitleChangedEventArgs(text.ToString()));
            }

            public string DocumentTitle {
                set {
                    documentTitle = value;
                    Show();
                }
            }

            public int Progress {
                set {
                    progress = value;
                    Show();
                }
            }
        }

        private readonly WebBrowserModeTitle webBrowserModeTitle;

        private bool useFlashActiveXControl = false;
        private CustomSecurityManager customSecurityManager = null;

        private void _WebBrowserMode(bool useFlashActiveXControl = false) {
            InitializeComponent();

            this.useFlashActiveXControl = useFlashActiveXControl;

            if (closableWebBrowser == null) {
                return;
            }
            
            closableWebBrowser.CanGoBackChanged += closableWebBrowser_CanGoBackChanged;
            closableWebBrowser.CanGoForwardChanged += closableWebBrowser_CanGoForwardChanged;
            closableWebBrowser.DocumentTitleChanged += closableWebBrowser_DocumentTitleChanged;
            closableWebBrowser.StatusTextChanged += closableWebBrowser_StatusTextChanged;
            closableWebBrowser.WebBrowserClose += closableWebBrowser_WebBrowserClose;
            closableWebBrowser.WebBrowserPaint += closableWebBrowser_WebBrowserPaint;

            statusBarStatusStrip.Renderer = new EndEllipsisTextRenderer();

            messageFilter = new MessageFilter(Back, Forward);

            lowLevelMouseProc = new HookProc(LowLevelMouseProc);
        }

        public WebBrowserMode(bool useFlashActiveXControl = false) {
            _WebBrowserMode(useFlashActiveXControl);
            webBrowserModeTitle = new WebBrowserModeTitle(TitleChanged);
        }

        public WebBrowserMode(Uri webBrowserURL, bool useFlashActiveXControl = false) {
            _WebBrowserMode(useFlashActiveXControl);
            webBrowserModeTitle = new WebBrowserModeTitle(TitleChanged);
            closableWebBrowser.Url = webBrowserURL;
        }

        ~WebBrowserMode() {
            if (closableWebBrowser == null) {
                return;
            }

            // the WebBrowserMode property must be nulled out, otherwise we
            // end up closing the current form when it's already closed
            // (browser reports being closed > we close the form and so on)
            // TODO
            closableWebBrowser.CanGoBackChanged -= closableWebBrowser_CanGoBackChanged;
            closableWebBrowser.CanGoForwardChanged -= closableWebBrowser_CanGoForwardChanged;
            closableWebBrowser.DocumentTitleChanged -= closableWebBrowser_DocumentTitleChanged;
            closableWebBrowser.StatusTextChanged -= closableWebBrowser_StatusTextChanged;
            closableWebBrowser.WebBrowserClose -= closableWebBrowser_WebBrowserClose;
            closableWebBrowser.WebBrowserPaint -= closableWebBrowser_WebBrowserPaint;
            closableWebBrowser.Dispose();
            closableWebBrowser = null;

            if (mouseHook != IntPtr.Zero) {
                if (UnhookWindowsHookEx(mouseHook)) {
                    mouseHook = IntPtr.Zero;
                    lowLevelMouseProc = null;
                }
            }
        }

        public void BrowserBack() {
            if (closableWebBrowser == null) {
                return;
            }

            closableWebBrowser.GoBack();
        }

        public void BrowserForward() {
            if (closableWebBrowser == null) {
                return;
            }

            closableWebBrowser.GoForward();
        }

        public void BrowserStop() {
            if (closableWebBrowser == null) {
                return;
            }

            closableWebBrowser.Stop();
        }

        public void BrowserRefresh() {
            if (closableWebBrowser == null) {
                return;
            }
            
            closableWebBrowser.Refresh();
        }

        public void BrowserSaveAsWebpage() {
            if (closableWebBrowser == null) {
                return;
            }

            closableWebBrowser.ShowSaveAsDialog();
        }

        public void BrowserPrint() {
            if (closableWebBrowser == null) {
                return;
            }

            closableWebBrowser.ShowPrintDialog();
        }

        public void BrowserGo(string url) {
            if (closableWebBrowser == null) {
                return;
            }

            if (String.IsNullOrEmpty(url)) {
                return;
            }

            Uri webBrowserURL;

            try {
                try {
                    webBrowserURL = new Uri(url);
                } catch (UriFormatException) {
                    webBrowserURL = new Uri(AddURLProtocol(url));
                }
            } catch (Exception) {
                return;
            }

            closableWebBrowser.Navigate(webBrowserURL);
        }

        public WebBrowserMode BrowserNewWindow() {
            // we don't want this window to be the parent, breaks fullscreen and not otherwise useful
            WebBrowserMode webBrowserForm = new WebBrowserMode(useFlashActiveXControl);
            webBrowserForm.Show(/*this*/);
            return webBrowserForm;
        }

        public void BrowserFullscreen() {
            Fullscreen = !Fullscreen;
        }

        private void Back(object sender, EventArgs e) {
            BrowserBack();
        }

        private void Forward(object sender, EventArgs e) {
            BrowserForward();
        }

        private void TitleChanged(object sender, TitleChangedEventArgs e) {
            Text = e.Text;
        }

        private void WebBrowserMode_Load(object sender, EventArgs e) {
            // default value is Redirector port
            /*
            short port = 8888;
            Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            if (configuration.AppSettings.Settings["Port"].Value != null) {
                string portString = configuration.AppSettings.Settings["Port"].Value;

                try {
                    port = short.Parse(portString);
                }
                catch (ArgumentNullException) { }
                catch (FormatException) { }
                catch (OverflowException) { }
            }
            */

            if (closableWebBrowser == null) {
                return;
            }

            try {
                //string portString = port.ToString();
                FlashpointProxy.Enable("http=127.0.0.1:22500;https=127.0.0.1:22500;ftp=127.0.0.1:22500");
            } catch (FlashpointProxyException ex) {
                // popup message box but allow through anyway
                LogExceptionToLauncher(ex);
                MessageBox.Show(Properties.Resources.FlashpointProxyNotEnabled, Properties.Resources.FlashpointSecurePlayer, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            try {
                customSecurityManager = new CustomSecurityManager(closableWebBrowser, useFlashActiveXControl);
            } catch (Win32Exception ex) {
                LogExceptionToLauncher(ex);
                ProgressManager.ShowError();
                MessageBox.Show(Properties.Resources.FailedCreateCustomSecurityManager, Properties.Resources.FlashpointSecurePlayer, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            if (closableWebBrowser.ActiveXInstance is SHDocVw.WebBrowser shDocVwWebBrowser) {
                // IE5
                shDocVwWebBrowser.NewWindow2 += ShDocVwWebBrowser_NewWindow2;
                // IE6
                shDocVwWebBrowser.NewWindow3 += ShDocVwWebBrowser_NewWindow3;
                shDocVwWebBrowser.WindowSetTop += ShDocVwWebBrowser_WindowSetTop;
                shDocVwWebBrowser.WindowSetLeft += ShDocVwWebBrowser_WindowSetLeft;
                shDocVwWebBrowser.WindowSetWidth += ShDocVwWebBrowser_WindowSetWidth;
                shDocVwWebBrowser.WindowSetHeight += ShDocVwWebBrowser_WindowSetHeight;
                shDocVwWebBrowser.WindowSetResizable += ShDocVwWebBrowser_WindowSetResizable;
                shDocVwWebBrowser.DownloadBegin += ShDocVwWebBrowser_DownloadBegin;
                shDocVwWebBrowser.DownloadComplete += ShDocVwWebBrowser_DownloadComplete;
            }

            BringToFront();
            Activate();
        }

        private void WebBrowserMode_FormClosing(object sender, FormClosingEventArgs e) {
            Hide();

            if (closableWebBrowser == null) {
                return;
            }

            customSecurityManager = null;

            if (closableWebBrowser.ActiveXInstance is SHDocVw.WebBrowser shDocVwWebBrowser) {
                // IE5
                shDocVwWebBrowser.NewWindow2 -= ShDocVwWebBrowser_NewWindow2;
                // IE6
                shDocVwWebBrowser.NewWindow3 -= ShDocVwWebBrowser_NewWindow3;
                shDocVwWebBrowser.WindowSetTop -= ShDocVwWebBrowser_WindowSetTop;
                shDocVwWebBrowser.WindowSetLeft -= ShDocVwWebBrowser_WindowSetLeft;
                shDocVwWebBrowser.WindowSetWidth -= ShDocVwWebBrowser_WindowSetWidth;
                shDocVwWebBrowser.WindowSetHeight -= ShDocVwWebBrowser_WindowSetHeight;
                shDocVwWebBrowser.WindowSetResizable -= ShDocVwWebBrowser_WindowSetResizable;
                shDocVwWebBrowser.DownloadBegin -= ShDocVwWebBrowser_DownloadBegin;
                shDocVwWebBrowser.DownloadComplete -= ShDocVwWebBrowser_DownloadComplete;
            }
        }

        private void WebBrowserMode_Activated(object sender, EventArgs e) {
            Application.AddMessageFilter(messageFilter);

            if (Fullscreen) {
                BringToFront();
            }
        }

        private void WebBrowserMode_Deactivate(object sender, EventArgs e) {
            Application.RemoveMessageFilter(messageFilter);

            if (Fullscreen) {
                WindowState = FormWindowState.Minimized;
            }
        }

        private object downloadCompletedLock = new object();
        private bool downloadCompleted = false;

        private bool DownloadCompleted {
            get {
                lock (downloadCompletedLock) {
                    return downloadCompleted;
                }
            }

            set {
                lock (downloadCompletedLock) {
                    downloadCompleted = value;
                }
            }
        }

        private void closableWebBrowser_ProgressChanged(object sender, WebBrowserProgressChangedEventArgs e) {
            if (e.CurrentProgress < 0) {
                DownloadCompleted = true;
            }

            if (DownloadCompleted) {
                return;
            }

            int progress = e.MaximumProgress > 0 ? (int)Math.Min((double)e.CurrentProgress / e.MaximumProgress * 100, 100) : 0;
            webBrowserModeTitle.Progress = progress;

            if (progress == 0) {
                progressToolStripProgressBar.Style = ProgressBarStyle.Marquee;
            } else {
                progressToolStripProgressBar.Style = ProgressBarStyle.Continuous;
            }

            progressToolStripProgressBar.Value = progress;
            progressToolStripProgressBar.ToolTipText = progress + "%";
        }

        private void closableWebBrowser_CanGoBackChanged(object sender, EventArgs e) {
            if (closableWebBrowser == null) {
                return;
            }

            backButton.Enabled = closableWebBrowser.CanGoBack;
        }

        private void closableWebBrowser_CanGoForwardChanged(object sender, EventArgs e) {
            if (closableWebBrowser == null) {
                return;
            }

            forwardButton.Enabled = closableWebBrowser.CanGoForward;
        }

        private void closableWebBrowser_DocumentTitleChanged(object sender, EventArgs e) {
            if (closableWebBrowser == null) {
                return;
            }

            webBrowserModeTitle.DocumentTitle = closableWebBrowser.DocumentTitle;
        }

        private void closableWebBrowser_StatusTextChanged(object sender, EventArgs e) {
            if (closableWebBrowser == null) {
                return;
            }

            // only set if not equal to prevent flashing tooltip
            if (statusToolStripStatusLabel.Text != closableWebBrowser.StatusText) {
                statusToolStripStatusLabel.Text = closableWebBrowser.StatusText;
            }
        }

        private void closableWebBrowser_Navigated(object sender, WebBrowserNavigatedEventArgs e) {
            if (closableWebBrowser == null) {
                return;
            }

            if (e.Url.Equals("about:blank")) {
                addressToolStripSpringTextBox.Text = String.Empty;
                return;
            }

            addressToolStripSpringTextBox.Text = e.Url.ToString();
        }

        private void closableWebBrowser_WebBrowserClose(object sender, EventArgs e) {
            Close();
        }

        private void closableWebBrowser_WebBrowserPaint(object sender, EventArgs e) {
            if (WindowState != FormWindowState.Maximized) {
                // lame fix: browser hangs when window.open top attribute > control height (why?)
                // Width, Height, and WindowState changes all work here
                // Width/Height are less obvious and Height doesn't cause text reflow
                Height--;
                Height++;
            }
        }

        public object PPDisp {
            get {
                if (closableWebBrowser == null) {
                    return null;
                }
                return closableWebBrowser.ActiveXInstance;
            }
        }

        private void ShDocVwWebBrowser_NewWindow2(ref object ppDisp, ref bool Cancel) {
            ppDisp = BrowserNewWindow().PPDisp;
            Cancel = false;
        }

        private void ShDocVwWebBrowser_NewWindow3(ref object ppDisp, ref bool Cancel, uint dwFlags, string bstrUrlContext, string bstrUrl) {
            ShDocVwWebBrowser_NewWindow2(ref ppDisp, ref Cancel);
        }

        private void ShDocVwWebBrowser_WindowSetTop(int Top) {
            this.Top = Top;
        }

        private void ShDocVwWebBrowser_WindowSetLeft(int Left) {
            this.Left = Left;
        }

        private void ShDocVwWebBrowser_WindowSetWidth(int Width) {
            if (closableWebBrowser == null) {
                return;
            }

            this.Width = this.Width - closableWebBrowser.Width + Width;
        }

        private void ShDocVwWebBrowser_WindowSetHeight(int Height) {
            if (closableWebBrowser == null) {
                return;
            }

            this.Height = this.Height - closableWebBrowser.Height + Height;
        }

        private void ShDocVwWebBrowser_WindowSetResizable(bool Resizable) {
            this.Resizable = Resizable;
        }

        private void ShDocVwWebBrowser_DownloadBegin() {
            if (closableWebBrowser == null) {
                return;
            }

            Control closableWebBrowserControl = closableWebBrowser as Control;

            if (closableWebBrowserControl == null) {
                return;
            }

            DownloadCompleted = false;
            webBrowserModeTitle.Progress = 0;
            progressToolStripProgressBar.Style = ProgressBarStyle.Marquee;
            progressToolStripProgressBar.Value = 0;
            progressToolStripProgressBar.ToolTipText = "0%";
            //UseWaitCursor = true;
            //closableWebBrowserControl.Enabled = false;
        }

        private void ShDocVwWebBrowser_DownloadComplete() {
            if (closableWebBrowser == null) {
                return;
            }

            Control closableWebBrowserControl = closableWebBrowser as Control;

            if (closableWebBrowserControl == null) {
                return;
            }

            DownloadCompleted = true;
            webBrowserModeTitle.Progress = -1;
            progressToolStripProgressBar.Style = ProgressBarStyle.Blocks;
            progressToolStripProgressBar.Value = 0;
            progressToolStripProgressBar.ToolTipText = String.Empty;
            //closableWebBrowserControl.Enabled = true;
            //UseWaitCursor = false;
        }

        private void backButton_Click(object sender, EventArgs e) {
            BrowserBack();
        }

        private void forwardButton_Click(object sender, EventArgs e) {
            BrowserForward();
        }

        private void stopButton_Click(object sender, EventArgs e) {
            BrowserStop();
        }

        private void refreshButton_Click(object sender, EventArgs e) {
            BrowserRefresh();
        }

        private void saveAsWebpageButton_Click(object sender, EventArgs e) {
            BrowserSaveAsWebpage();
        }

        private void printButton_Click(object sender, EventArgs e) {
            BrowserPrint();
        }

        private bool addressToolStripSpringTextBoxEntered = false;

        private void addressToolStripSpringTextBox_Click(object sender, EventArgs e) {
            if (addressToolStripSpringTextBoxEntered) {
                addressToolStripSpringTextBoxEntered = false;
                addressToolStripSpringTextBox.SelectAll();
            }
        }

        private void addressToolStripTextBox_Paint(object sender, PaintEventArgs e) {
            // manually draw the border so the text is vertically aligned correctly
            Rectangle borderRectangle = new Rectangle(0, 1, addressToolStripSpringTextBox.Width - 1, addressToolStripSpringTextBox.Height - 3);
            e.Graphics.FillRectangle(SystemBrushes.Window, borderRectangle);
            e.Graphics.DrawRectangle(SystemPens.WindowFrame, borderRectangle);
        }

        private void addressToolStripSpringTextBox_Enter(object sender, EventArgs e) {
            addressToolStripSpringTextBoxEntered = true;
        }

        private void addressToolStripTextBox_KeyDown(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Enter) {
                e.SuppressKeyPress = true;
                BrowserGo(addressToolStripSpringTextBox.Text);
            }
        }

        private void goButton_Click(object sender, EventArgs e) {
            BrowserGo(addressToolStripSpringTextBox.Text);
        }

        private void newWindowButton_Click(object sender, EventArgs e) {
            BrowserNewWindow();
        }

        private void fullscreenButton_Click(object sender, EventArgs e) {
            BrowserFullscreen();
        }

        private void exitFullscreenLabelTimer_Tick(object sender, EventArgs e) {
            ExitFullscreenLabelTimer = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // don't disable keys on e.g. the address bar
            if (ActiveControl == closableWebBrowser) {
                switch (keyData) {
                    case Keys.Back:
                    case Keys.Control | Keys.Left:
                    case Keys.Alt | Keys.Left:
                    case Keys.BrowserBack:
                    BrowserBack();
                    return true;
                    case Keys.Control | Keys.Right:
                    case Keys.Alt | Keys.Right:
                    case Keys.BrowserForward:
                    BrowserForward();
                    return true;
                    case Keys.Escape:
                    case Keys.BrowserStop:
                    BrowserStop();
                    return true;
                    case Keys.F5:
                    case Keys.Control | Keys.R:
                    case Keys.BrowserRefresh:
                    BrowserRefresh();
                    return true;
                    case Keys.Control | Keys.S:
                    BrowserSaveAsWebpage();
                    return true;
                    case Keys.Control | Keys.P:
                    BrowserPrint();
                    return true;
                    case Keys.Control | Keys.N:
                    BrowserNewWindow();
                    return true;
                    case Keys.F11:
                    case Keys.Alt | Keys.Enter:
                    BrowserFullscreen();
                    return true;
                }
            }
            // don't forget to call the base!
            // (better fix for Atmosphere plugin)
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}