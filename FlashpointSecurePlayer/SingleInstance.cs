﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using static FlashpointSecurePlayer.Shared;
using static FlashpointSecurePlayer.Shared.Exceptions;
using static FlashpointSecurePlayer.Shared.FlashpointSecurePlayerSection.TemplatesElementCollection;
using static FlashpointSecurePlayer.Shared.FlashpointSecurePlayerSection.TemplatesElementCollection.TemplateElement;
using static FlashpointSecurePlayer.Shared.FlashpointSecurePlayerSection.TemplatesElementCollection.TemplateElement.ModificationsElement;
using static FlashpointSecurePlayer.Shared.FlashpointSecurePlayerSection.TemplatesElementCollection.TemplateElement.ModificationsElement.SingleInstanceElement;

namespace FlashpointSecurePlayer {
    public class SingleInstance : Modifications {
        public SingleInstance(EventHandler importStart, EventHandler importStop) : base(importStart, importStop) { }

        // function to create a real MessageBox which
        // automatically closes upon completion of tasks
        private DialogResult? ShowClosableMessageBox(Task[] tasks, string text, string caption, MessageBoxButtons messageBoxButtons, MessageBoxIcon messageBoxIcon) {
            Form closableForm = new Form() {
                Size = new Size(0, 0)
            };

            closableForm.BringToFront();

            // need this because dialogResult defaults to Cancel when
            // we want it to default to null
            bool dialogResultSet = true;

            Task.WhenAll(tasks).ContinueWith(delegate (Task antecedentTask) {
                HandleAntecedentTask(antecedentTask);

                // this closes the form hosting the Message Box and
                // causes it to stop blocking
                closableForm.Close();
                dialogResultSet = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());

            // this line blocks execution, but the task above causes it to stop blocking
            DialogResult dialogResult = MessageBox.Show(closableForm, text, caption, messageBoxButtons, messageBoxIcon);

            if (!dialogResultSet) {
                return null;
            }
            return dialogResult;
        }

        // single task version
        private DialogResult? ShowClosableMessageBox(Task task, string text, string caption, MessageBoxButtons messageBoxButtons, MessageBoxIcon messageBoxIcon) {
            return ShowClosableMessageBox(new Task[] { task }, text, caption, messageBoxButtons, messageBoxIcon);
        }

        public override void Activate(string templateName) {
            base.Activate(templateName);

            if (String.IsNullOrEmpty(TemplateName)) {
                // no argument
                return;
            }

            TemplateElement templateElement = GetTemplateElement(false, TemplateName);

            if (templateElement == null) {
                return;
            }

            ModeElement modeElement = templateElement.Mode;
            ModificationsElement modificationsElement = templateElement.Modifications;

            if (!modificationsElement.ElementInformation.IsPresent) {
                return;
            }

            SingleInstanceElement singleInstanceElement = modificationsElement.SingleInstance;

            if (!singleInstanceElement.ElementInformation.IsPresent) {
                return;
            }

            string executable = singleInstanceElement.Executable;

            if (String.IsNullOrEmpty(executable)) {
                string[] argv = GetCommandLineToArgv(Environment.ExpandEnvironmentVariables(modeElement.CommandLine), out int argc);

                if (argc > 0) {
                    executable = argv[0];
                }
            }

            if (String.IsNullOrEmpty(executable)) {
                return;
            }
            
            //string[] argv = CommandLineToArgv(executablePath, out int argc);

            Process[] processesByName = null;
            Stack<Process> processesByNameStrict = null;
            string processName = null;
            // GetProcessesByName can't have extension (stupidly)
            string activeProcessName = Path.GetFileNameWithoutExtension(executable);
            bool clear = false;

            do {
                // the strict list is the one which will be checked against for real
                processesByName = Process.GetProcessesByName(activeProcessName);

                if (processesByName == null) {
                    return;
                }

                if (singleInstanceElement.Strict) {
                    processesByNameStrict = new Stack<Process>();

                    for (int i = 0; i < processesByName.Length; i++) {
                        if (processesByName[i] != null) {
                            processName = GetProcessName(processesByName[i]).ToString();

                            clear = false;

                            try {
                                if (!ComparePaths(executable, processName)) {
                                    clear = true;
                                }
                            } catch {
                                // fail silently
                            }

                            if (clear) {
                                processesByName[i].Dispose();
                            } else {
                                processesByNameStrict.Push(processesByName[i]);
                            }

                            processesByName[i] = null;
                        }
                    }
                } else {
                    processesByNameStrict = new Stack<Process>(processesByName);
                }

                processesByName = null;

                // don't allow preceding further until
                // all processes with the same name have been killed
                if (processesByNameStrict.Any()) {
                    DialogResult? dialogResult = ShowClosableMessageBox(Task.Run(delegate () {
                        while (processesByNameStrict.Any()) {
                            using (Process processByNameStrict = processesByNameStrict.Peek()) {
                                try {
                                    if (processByNameStrict != null) {
                                        processByNameStrict.WaitForExit();
                                    }
                                } finally {
                                    processesByNameStrict.Pop();
                                }
                            }
                        }
                    }), String.Format(Properties.Resources.ProcessCompatibilityConflict, activeProcessName), Properties.Resources.FlashpointSecurePlayer, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.Cancel) {
                        Application.Exit();
                        throw new InvalidModificationException("The operation was aborted by the user.");
                    }
                }
                // continue this process until the problem is resolved
            } while (processesByNameStrict.Any());
        }
    }
}
