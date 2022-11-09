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
        private HookProc lowLevelMouseProc = null;
        private IntPtr mouseHook = IntPtr.Zero;

        private IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0) {
                // always confirm the message first so we don't do unnecessary work
                if (wParam.ToInt32() == WM_MOUSEMOVE) {
                    if (Fullscreen) {
                        // this is checked in LowLevelMouseProc because
                        // otherwise plugins such as Viscape which
                        // create their own window can steal the
                        // mouse move event
                        // it cannot happen in PreFilterMessage!
                        // our window may not even get these messages
                        // all that matters is the mouse position, regardless
                        // of if our window is active
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
        private Point fullscreenLocation;
        private Size fullscreenSize;
        private FormBorderStyle fullscreenFormBorderStyle = FormBorderStyle.Sizable;
        private FormWindowState fullscreenWindowState = FormWindowState.Maximized;

        private Point closableWebBrowserLocation;
        private Size closableWebBrowserSize;

        public bool Fullscreen {
            get {
                return fullscreen;
            }

            set {
                if (closableWebBrowser == null) {
                    return;
                }

                if (fullscreen == value) {
                    return;
                }

                fullscreen = value;

                if (fullscreen) {
                    // make strips invisible so the Closable Web Browser can be Docked
                    fullscreenButton.Checked = true;
                    toolBarToolStrip.Visible = false;
                    statusBarStatusStrip.Visible = false;

                    // get the original properties before modifying them
                    fullscreenLocation = Location;
                    fullscreenSize = Size;
                    fullscreenFormBorderStyle = FormBorderStyle;
                    fullscreenWindowState = WindowState;

                    closableWebBrowserLocation = closableWebBrowser.Location;
                    closableWebBrowserSize = closableWebBrowser.Size;
                    closableWebBrowser.Dock = DockStyle.Fill;

                    // need to do this first to have an effect if starting maximized
                    WindowState = FormWindowState.Normal;
                    // disable resizing
                    FormBorderStyle = FormBorderStyle.None;
                    // enter fullscreen
                    WindowState = FormWindowState.Maximized;

                    // set Windows Hook for toolbar
                    if (mouseHook == IntPtr.Zero && lowLevelMouseProc != null) {
                        mouseHook = SetWindowsHookEx(HookType.WH_MOUSE_LL, lowLevelMouseProc, IntPtr.Zero, 0);
                    }

                    // show "Press F11 to Exit Fullscreen" label
                    ExitFullscreenLabelTimer = true;

                    // commit by bringing the window to the front
                    BringToFront();
                } else {
                    // hide "Press F11 to Exit Fullscreen" label
                    ExitFullscreenLabelTimer = false;

                    // unhook Windows Hook for toolbar
                    if (mouseHook != IntPtr.Zero) {
                        if (UnhookWindowsHookEx(mouseHook)) {
                            mouseHook = IntPtr.Zero;
                        }
                    }

                    // make strips visible so the Closable Web Browser can be Anchored
                    fullscreenButton.Checked = false;
                    toolBarToolStrip.Visible = true;
                    statusBarStatusStrip.Visible = true;

                    // need to do this first to reset the window to its former size
                    FormBorderStyle = FormBorderStyle.Sizable;
                    // exit fullscreen
                    WindowState = FormWindowState.Normal;

                    // reset to the original properties before modifying them
                    FormBorderStyle = fullscreenFormBorderStyle;
                    WindowState = fullscreenWindowState;
                    Location = fullscreenLocation;
                    Size = fullscreenSize;
                    
                    closableWebBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                    closableWebBrowser.Location = closableWebBrowserLocation;
                    closableWebBrowser.Size = closableWebBrowserSize;

                    // commit by bringing the window to the front
                    BringToFront();
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
            private readonly EventHandler back;
            private readonly EventHandler forward;

            public MessageFilter(EventHandler back, EventHandler forward) {
                this.back = back;
                this.forward = forward;
            }

            protected virtual void OnBack(EventArgs e) {
                EventHandler eventHandler = back;

                if (eventHandler == null) {
                    return;
                }

                eventHandler(this, e);
            }

            protected virtual void OnForward(EventArgs e) {
                EventHandler eventHandler = forward;

                if (eventHandler == null) {
                    return;
                }

                eventHandler(this, e);
            }

            [SecurityPermission(SecurityAction.Demand)]
            public bool PreFilterMessage(ref Message m) {
                // this happens in PreFilterMessage because
                // the mouse back/forward buttons should only
                // have an effect if our window is active
                // for example, if there is a popup, the
                // mouse buttons shouldn't navigate both the
                // main and popup windows
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
            private readonly EventHandler<TitleChangedEventArgs> titleChanged;

            private readonly string applicationTitle = "Flashpoint Secure Player";
            private string documentTitle = null;
            private int progress = -1;

            public WebBrowserModeTitle(EventHandler<TitleChangedEventArgs> titleChanged) {
                this.titleChanged = titleChanged;
                applicationTitle += " " + typeof(WebBrowserModeTitle).Assembly.GetName().Version;

                Show();
            }

            protected virtual void OnTitleChanged(TitleChangedEventArgs e) {
                EventHandler<TitleChangedEventArgs> eventHandler = titleChanged;

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

        private bool UseFlashActiveXControl { get; set; } = false;
        private CustomSecurityManager customSecurityManager = null;

        private void _WebBrowserMode(bool useFlashActiveXControl = false) {
            InitializeComponent();

            UseFlashActiveXControl = useFlashActiveXControl;

            if (closableWebBrowser == null) {
                closableWebBrowser = new ClosableWebBrowser();
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

            if (closableWebBrowser != null) {
                closableWebBrowser.Url = webBrowserURL;
            }
        }

        ~WebBrowserMode() {
            // the WebBrowserClose event must be disabled, otherwise we
            // end up closing the current form when it's already closed
            // (browser reports being closed > we close the form and so on)
            if (closableWebBrowser != null) {
                closableWebBrowser.CanGoBackChanged -= closableWebBrowser_CanGoBackChanged;
                closableWebBrowser.CanGoForwardChanged -= closableWebBrowser_CanGoForwardChanged;
                closableWebBrowser.DocumentTitleChanged -= closableWebBrowser_DocumentTitleChanged;
                closableWebBrowser.StatusTextChanged -= closableWebBrowser_StatusTextChanged;

                closableWebBrowser.WebBrowserClose -= closableWebBrowser_WebBrowserClose;
                closableWebBrowser.WebBrowserPaint -= closableWebBrowser_WebBrowserPaint;

                closableWebBrowser.Dispose();
                closableWebBrowser = null;
            }

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

            //Fullscreen = false;
            closableWebBrowser.ShowSaveAsDialog();
        }

        public void BrowserPrint() {
            if (closableWebBrowser == null) {
                return;
            }

            //Fullscreen = false;
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
                webBrowserURL = new Uri(AddURLProtocol(url));
            } catch {
                return;
            }

            closableWebBrowser.Navigate(webBrowserURL);
        }

        public WebBrowserMode BrowserNewWindow() {
            //Fullscreen = false;

            // we don't want this window to be the parent, breaks fullscreen and not otherwise useful
            WebBrowserMode webBrowserForm = new WebBrowserMode(UseFlashActiveXControl);
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
                customSecurityManager = new CustomSecurityManager(closableWebBrowser, UseFlashActiveXControl);
            } catch (Exception ex) {
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
                if (WindowState == FormWindowState.Minimized) {
                    BringToFront();
                }
            }
        }

        private void WebBrowserMode_Deactivate(object sender, EventArgs e) {
            Application.RemoveMessageFilter(messageFilter);

            if (Fullscreen) {
                IntPtr activeWindow = GetActiveWindow();

                // if the active window is zero, this process doesn't have an active window
                // if this process has the foreground window, it'll be the active window
                if (activeWindow != IntPtr.Zero && activeWindow == GetForegroundWindow()) {
                    // this process opened a window
                    if (IsWindowEnabled(Handle)) {
                        // the new window isn't a dialog
                        Fullscreen = false;
                        return;
                    }
                    return;
                }

                // another process opened a window
                WindowState = FormWindowState.Minimized;
            }
        }

        private void closableWebBrowser_Navigated(object sender, WebBrowserNavigatedEventArgs e) {
            if (e.Url.Equals("about:blank")) {
                addressToolStripSpringTextBox.Text = String.Empty;
                return;
            }

            addressToolStripSpringTextBox.Text = e.Url.ToString();
        }

        private readonly object downloadCompletedLock = new object();
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

        private void closableWebBrowser_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e) {
            // usually, Ctrl + S/P/N are handled automatically by the
            // browser control, but we actually don't want this because we
            // need to exit fullscreen first so the dialogs can open on top
            // these keys are handled on the PreviewKeyDown event, which means
            // the browser control itself, and ProcessCmdKey, never see them
            // this is fine, because these shortcuts are always active
            switch (e.KeyData) {
                case Keys.Control | Keys.S:
                e.IsInputKey = true;
                BrowserSaveAsWebpage();
                break;
                case Keys.Control | Keys.P:
                e.IsInputKey = true;
                BrowserPrint();
                break;
                case Keys.Control | Keys.N:
                e.IsInputKey = true;
                BrowserNewWindow();
                break;
            }
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
            
            statusToolStripStatusLabel.Text = closableWebBrowser.StatusText;
        }

        private void closableWebBrowser_WebBrowserClose(object sender, EventArgs e) {
            Close();
        }

        private void closableWebBrowser_WebBrowserPaint(object sender, EventArgs e) {
            // lame fix: browser hangs when window.open top attribute > control height (why?)
            // Width, Height, and WindowState changes all work here
            // Width/Height are less obvious and Height doesn't cause text reflow
            if (WindowState != FormWindowState.Maximized) {
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

        // although we don't need closableWebBrowser for the WindowSetTop/WindowSetLeft functions
        // it'd be weird if they worked but the WindowSetWidth/WindowSetHeight functions did not
        // so we check if it's null anyway
        private void ShDocVwWebBrowser_WindowSetTop(int top) {
            if (closableWebBrowser == null) {
                return;
            }

            Fullscreen = false;
            Top = top;
        }

        private void ShDocVwWebBrowser_WindowSetLeft(int left) {
            if (closableWebBrowser == null) {
                return;
            }

            Fullscreen = false;
            Left = left;
        }

        private void ShDocVwWebBrowser_WindowSetWidth(int width) {
            if (closableWebBrowser == null) {
                return;
            }

            Fullscreen = false;
            Width += width - closableWebBrowser.Width;
        }

        private void ShDocVwWebBrowser_WindowSetHeight(int height) {
            if (closableWebBrowser == null) {
                return;
            }

            Fullscreen = false;
            Height += height - closableWebBrowser.Height;
        }

        private void ShDocVwWebBrowser_WindowSetResizable(bool resizable) {
            if (closableWebBrowser == null) {
                return;
            }

            Fullscreen = false;

            if (resizable) {
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = true;
            } else {
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
            }
        }

        private void ShDocVwWebBrowser_DownloadBegin() {
            /*
            if (closableWebBrowser == null) {
                return;
            }

            Control closableWebBrowserControl = closableWebBrowser as Control;

            if (closableWebBrowserControl == null) {
                return;
            }
            */

            DownloadCompleted = false;
            webBrowserModeTitle.Progress = 0;
            progressToolStripProgressBar.Style = ProgressBarStyle.Marquee;
            progressToolStripProgressBar.Value = 0;
            progressToolStripProgressBar.ToolTipText = "0%";
            //UseWaitCursor = true;
            //closableWebBrowserControl.Enabled = false;
        }

        private void ShDocVwWebBrowser_DownloadComplete() {
            /*
            if (closableWebBrowser == null) {
                return;
            }

            Control closableWebBrowserControl = closableWebBrowser as Control;

            if (closableWebBrowserControl == null) {
                return;
            }
            */

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
            if (ActiveControl != null && ActiveControl == closableWebBrowser) {
                // IMPORTANT: these controls (such as Backspace to navigate back)
                // must be handled here in ProcessCmdKey, not on PreviewKeyDown!
                // otherwise, controls on the page won't recieve the input
                // for example, while editing a textbox, Backspace should
                // erase characters, not navigate back
                // ProcessCmdKey handles this properly, PreviewKeyDown would circumvent this
                // Ctrl + S/P/N are exempt from this and are handled
                // on the PreviewKeyDown event instead
                switch (keyData) {
                    case Keys.Back:
                    case Keys.Alt | Keys.Left:
                    case Keys.BrowserBack:
                    BrowserBack();
                    return true;
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
                    /*
                    case Keys.Control | Keys.S:
                    BrowserSaveAsWebpage();
                    return true;
                    case Keys.Control | Keys.P:
                    BrowserPrint();
                    return true;
                    case Keys.Control | Keys.N:
                    BrowserNewWindow();
                    return true;
                    */
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