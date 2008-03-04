///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

#if ENABLE_PROGRESS_FORM

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RunParallel
{
    public class ProgressForm : Form
    {
        public ProgressForm()
        {
            InitializeComponent();
        }

        private void UpdateTimerTick(object sender, EventArgs e)
        {
            Program.UpdateProgressGuiThread();
        }


        bool _allowclose;

        public void PublicClose()
        {
            Debug.WriteLine("gui - PublicClose");
            _allowclose = true;
            this.Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!_allowclose)
            {
                Debug.WriteLine("gui - too early to close");
                e.Cancel = true;
            }
            else
            {

                Debug.WriteLine("gui - closing");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            this._UpdateTimer.Enabled = false;
            Debug.WriteLine("gui - closed");
        }


        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.ColumnHeader columnHeader1;
            System.Windows.Forms.ColumnHeader columnHeader2;
            System.Windows.Forms.ColumnHeader columnHeader3;
            this.WorkerListView = new System.Windows.Forms.ListView();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.OverallProgressBar = new System.Windows.Forms.ProgressBar();
            this._TotalTasksLabel = new System.Windows.Forms.Label();
            this._TasksRemainingLabel = new System.Windows.Forms.Label();
            this._UpdateTimer = new System.Windows.Forms.Timer(this.components);
            columnHeader1 = new System.Windows.Forms.ColumnHeader();
            columnHeader2 = new System.Windows.Forms.ColumnHeader();
            columnHeader3 = new System.Windows.Forms.ColumnHeader();
            this.SuspendLayout();
            // 
            // WorkerListView
            // 
            this.WorkerListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.WorkerListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            columnHeader1,
            columnHeader2,
            columnHeader3});
            this.WorkerListView.Location = new System.Drawing.Point(14, 98);
            this.WorkerListView.Name = "WorkerListView";
            this.WorkerListView.Size = new System.Drawing.Size(728, 157);
            this.WorkerListView.TabIndex = 0;
            this.WorkerListView.UseCompatibleStateImageBehavior = false;
            this.WorkerListView.View = System.Windows.Forms.View.Details;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(14, 17);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(62, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Total tasks:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(14, 41);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(78, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Tasks queued:";
            // 
            // OverallProgressBar
            // 
            this.OverallProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.OverallProgressBar.Location = new System.Drawing.Point(249, 41);
            this.OverallProgressBar.Name = "OverallProgressBar";
            this.OverallProgressBar.Size = new System.Drawing.Size(493, 21);
            this.OverallProgressBar.TabIndex = 3;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "Thread";
            columnHeader1.Width = 67;
            // 
            // columnHeader2
            // 
            columnHeader2.Text = "Current Task";
            columnHeader2.Width = 148;
            // 
            // columnHeader3
            // 
            columnHeader3.Text = "Command Line";
            columnHeader3.Width = 470;
            // 
            // _TotalTasksLabel
            // 
            this._TotalTasksLabel.AutoSize = true;
            this._TotalTasksLabel.Location = new System.Drawing.Point(116, 17);
            this._TotalTasksLabel.Name = "_TotalTasksLabel";
            this._TotalTasksLabel.Size = new System.Drawing.Size(33, 13);
            this._TotalTasksLabel.TabIndex = 4;
            this._TotalTasksLabel.Text = "(total)";
            // 
            // _TasksRemainingLabel
            // 
            this._TasksRemainingLabel.AutoSize = true;
            this._TasksRemainingLabel.Location = new System.Drawing.Point(116, 41);
            this._TasksRemainingLabel.Name = "_TasksRemainingLabel";
            this._TasksRemainingLabel.Size = new System.Drawing.Size(58, 13);
            this._TasksRemainingLabel.TabIndex = 4;
            this._TasksRemainingLabel.Text = "(remaining)";
            // 
            // _UpdateTimer
            // 
            this._UpdateTimer.Enabled = true;
            this._UpdateTimer.Interval = 250;
            this._UpdateTimer.Tick += new System.EventHandler(this.UpdateTimerTick);
            // 
            // ProgressForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(754, 266);
            this.Controls.Add(this._TasksRemainingLabel);
            this.Controls.Add(this._TotalTasksLabel);
            this.Controls.Add(this.OverallProgressBar);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.WorkerListView);
            this.MaximizeBox = false;
            this.Name = "ProgressForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.Text = "Run Parallel";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        internal System.Windows.Forms.ListView WorkerListView;
        internal System.Windows.Forms.ProgressBar OverallProgressBar;
        internal System.Windows.Forms.Label _TotalTasksLabel;
        internal System.Windows.Forms.Label _TasksRemainingLabel;
        private System.Windows.Forms.Timer _UpdateTimer;
    }
}

#endif
