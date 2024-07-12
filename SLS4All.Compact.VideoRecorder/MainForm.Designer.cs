// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.VideoRecorder
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            _addressBox = new TextBox();
            label2 = new Label();
            _filenameBox = new TextBox();
            _browseButton = new Button();
            _thermoPicture = new PictureBox();
            _videoPicture = new PictureBox();
            _startStopButton = new Button();
            _fpsUpDown = new NumericUpDown();
            label3 = new Label();
            _statusBox = new TextBox();
            ((System.ComponentModel.ISupportInitialize)_thermoPicture).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_videoPicture).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_fpsUpDown).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(145, 15);
            label1.TabIndex = 0;
            label1.Text = "SLS4All Compact Address:";
            // 
            // _addressBox
            // 
            _addressBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _addressBox.Location = new Point(163, 6);
            _addressBox.Name = "_addressBox";
            _addressBox.Size = new Size(645, 23);
            _addressBox.TabIndex = 1;
            _addressBox.Text = "http://192.168.1.105:5000/";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(58, 42);
            label2.Name = "label2";
            label2.Size = new Size(99, 15);
            label2.TabIndex = 3;
            label2.Text = "Output video file:";
            // 
            // _filenameBox
            // 
            _filenameBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _filenameBox.Location = new Point(163, 39);
            _filenameBox.Name = "_filenameBox";
            _filenameBox.Size = new Size(645, 23);
            _filenameBox.TabIndex = 4;
            _filenameBox.Text = "recording.mp4";
            // 
            // _browseButton
            // 
            _browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _browseButton.Location = new Point(814, 39);
            _browseButton.Name = "_browseButton";
            _browseButton.Size = new Size(75, 23);
            _browseButton.TabIndex = 5;
            _browseButton.Text = "Browse";
            _browseButton.UseVisualStyleBackColor = true;
            _browseButton.Click += _browseButton_Click;
            // 
            // _thermoPicture
            // 
            _thermoPicture.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _thermoPicture.BorderStyle = BorderStyle.FixedSingle;
            _thermoPicture.Location = new Point(12, 96);
            _thermoPicture.Name = "_thermoPicture";
            _thermoPicture.Size = new Size(475, 349);
            _thermoPicture.SizeMode = PictureBoxSizeMode.Zoom;
            _thermoPicture.TabIndex = 6;
            _thermoPicture.TabStop = false;
            // 
            // _videoPicture
            // 
            _videoPicture.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            _videoPicture.BorderStyle = BorderStyle.FixedSingle;
            _videoPicture.Location = new Point(495, 96);
            _videoPicture.Name = "_videoPicture";
            _videoPicture.Size = new Size(475, 349);
            _videoPicture.SizeMode = PictureBoxSizeMode.Zoom;
            _videoPicture.TabIndex = 7;
            _videoPicture.TabStop = false;
            // 
            // _startStopButton
            // 
            _startStopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _startStopButton.Location = new Point(895, 39);
            _startStopButton.Name = "_startStopButton";
            _startStopButton.Size = new Size(75, 23);
            _startStopButton.TabIndex = 8;
            _startStopButton.Text = "Start";
            _startStopButton.UseVisualStyleBackColor = true;
            _startStopButton.Click += _startStopButton_Click;
            // 
            // _fpsUpDown
            // 
            _fpsUpDown.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _fpsUpDown.Location = new Point(895, 6);
            _fpsUpDown.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            _fpsUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _fpsUpDown.Name = "_fpsUpDown";
            _fpsUpDown.Size = new Size(75, 23);
            _fpsUpDown.TabIndex = 9;
            _fpsUpDown.Value = new decimal(new int[] { 15, 0, 0, 0 });
            // 
            // label3
            // 
            label3.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            label3.AutoSize = true;
            label3.Location = new Point(860, 9);
            label3.Name = "label3";
            label3.Size = new Size(29, 15);
            label3.TabIndex = 10;
            label3.Text = "FPS:";
            // 
            // _statusBox
            // 
            _statusBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _statusBox.Font = new Font("Segoe UI", 7F);
            _statusBox.Location = new Point(12, 70);
            _statusBox.Name = "_statusBox";
            _statusBox.ReadOnly = true;
            _statusBox.Size = new Size(958, 20);
            _statusBox.TabIndex = 11;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(982, 450);
            Controls.Add(_statusBox);
            Controls.Add(label3);
            Controls.Add(_fpsUpDown);
            Controls.Add(_startStopButton);
            Controls.Add(_videoPicture);
            Controls.Add(_thermoPicture);
            Controls.Add(_browseButton);
            Controls.Add(_filenameBox);
            Controls.Add(label2);
            Controls.Add(_addressBox);
            Controls.Add(label1);
            MinimizeBox = false;
            MinimumSize = new Size(500, 300);
            Name = "MainForm";
            Text = "SLS4All Video Recorder";
            SizeChanged += MainForm_SizeChanged;
            ((System.ComponentModel.ISupportInitialize)_thermoPicture).EndInit();
            ((System.ComponentModel.ISupportInitialize)_videoPicture).EndInit();
            ((System.ComponentModel.ISupportInitialize)_fpsUpDown).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox _addressBox;
        private Label label2;
        private TextBox _filenameBox;
        private Button _browseButton;
        private PictureBox _thermoPicture;
        private PictureBox _videoPicture;
        private Button _startStopButton;
        private NumericUpDown _fpsUpDown;
        private Label label3;
        private TextBox _statusBox;
    }
}
