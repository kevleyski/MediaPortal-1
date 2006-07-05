#region Copyright (C) 2005-2006 Team MediaPortal

/* 
 *	Copyright (C) 2005-2006 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */

#endregion

using System;
using System.IO;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using MediaPortal.Util;
#pragma warning disable 108
namespace MediaPortal.Configuration.Sections
{
  public class Skin : MediaPortal.Configuration.SectionSettings
  {
    const string SkinDirectory = @"skin\";

    private MediaPortal.UserInterface.Controls.MPGroupBox groupBox1;
    private System.Windows.Forms.ListView listViewAvailableSkins;
    private System.Windows.Forms.ColumnHeader colName;
    private System.Windows.Forms.ColumnHeader colVersion;
    private System.Windows.Forms.PictureBox previewPictureBox;
    private System.ComponentModel.IContainer components = null;

    public Skin()
      : this("Skin")
    {
    }

    public Skin(string name)
      : base(name)
    {

      // This call is required by the Windows Form Designer.
      InitializeComponent();

      //
      // Load available skins
      //
      listViewAvailableSkins.Items.Clear();

      if (Directory.Exists(SkinDirectory))
      {
        string[] skinFolders = Directory.GetDirectories(SkinDirectory, "*.*");

        foreach (string skinFolder in skinFolders)
        {
          bool isInvalidDirectory = false;
          string[] invalidDirectoryNames = new string[] { "cvs" };

          string directoryName = skinFolder.Substring(SkinDirectory.Length);

          if (directoryName != null && directoryName.Length > 0)
          {
            foreach (string invalidDirectory in invalidDirectoryNames)
            {
              if (invalidDirectory.Equals(directoryName.ToLower()))
              {
                isInvalidDirectory = true;
                break;
              }
            }

            if (isInvalidDirectory == false)
            {
              //
              // Check if we have a home.xml located in the directory, if so we consider it as a
              // valid skin directory
              //
              string filename = Path.Combine(SkinDirectory, Path.Combine(directoryName, "references.xml"));
              if (File.Exists(filename))
              {
                XmlDocument doc = new XmlDocument();
                doc.Load(filename);
                XmlNode node = doc.SelectSingleNode("/controls/skin/version");
                ListViewItem item = listViewAvailableSkins.Items.Add(directoryName);
                if (node != null && node.InnerText != null)
                  item.SubItems.Add(node.InnerText);
                else
                  item.SubItems.Add("?");
              }
            }
          }
        }
      }
    }

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (components != null)
        {
          components.Dispose();
        }
      }
      base.Dispose(disposing);
    }

    /// <summary>
    /// 
    /// </summary>
    public override void LoadSettings()
    {
      using (MediaPortal.Profile.Settings xmlreader = new MediaPortal.Profile.Settings("MediaPortal.xml"))
      {
        string currentSkin = xmlreader.GetValueAsString("skin", "name", "BlueTwo");

        //
        // Make sure the skin actually exists before setting it as the current skin
        //
        foreach (ListViewItem item in listViewAvailableSkins.Items)
        {
          if (item.SubItems[0].Text.Equals(currentSkin))
          {
            item.Selected = true;
            break;
          }
        }
      }
    }

    public override void SaveSettings()
    {
      if (listViewAvailableSkins.SelectedItems.Count == 0) return;
      using (MediaPortal.Profile.Settings xmlwriter = new MediaPortal.Profile.Settings("MediaPortal.xml"))
      {
        string prevSkin = xmlwriter.GetValueAsString("skin", "name", "BlueTwo");
        if (prevSkin != listViewAvailableSkins.SelectedItems[0].Text)
        {
          MediaPortal.Util.Utils.DeleteFiles(@"skin\" + listViewAvailableSkins.Text + @"\fonts", "*");
        }
        xmlwriter.SetValue("skin", "name", listViewAvailableSkins.SelectedItems[0].Text);
      }
    }

    #region Designer generated code
    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      this.groupBox1 = new MediaPortal.UserInterface.Controls.MPGroupBox();
      this.listViewAvailableSkins = new System.Windows.Forms.ListView();
      this.colName = new System.Windows.Forms.ColumnHeader();
      this.colVersion = new System.Windows.Forms.ColumnHeader();
      this.previewPictureBox = new System.Windows.Forms.PictureBox();
      this.groupBox1.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.previewPictureBox)).BeginInit();
      this.SuspendLayout();
      // 
      // groupBox1
      // 
      this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                  | System.Windows.Forms.AnchorStyles.Left)
                  | System.Windows.Forms.AnchorStyles.Right)));
      this.groupBox1.Controls.Add(this.listViewAvailableSkins);
      this.groupBox1.Controls.Add(this.previewPictureBox);
      this.groupBox1.Location = new System.Drawing.Point(0, 0);
      this.groupBox1.Name = "groupBox1";
      this.groupBox1.Size = new System.Drawing.Size(472, 408);
      this.groupBox1.TabIndex = 0;
      this.groupBox1.TabStop = false;
      // 
      // listViewAvailableSkins
      // 
      this.listViewAvailableSkins.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                  | System.Windows.Forms.AnchorStyles.Right)));
      this.listViewAvailableSkins.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colName,
            this.colVersion});
      this.listViewAvailableSkins.FullRowSelect = true;
      this.listViewAvailableSkins.HideSelection = false;
      this.listViewAvailableSkins.Location = new System.Drawing.Point(16, 24);
      this.listViewAvailableSkins.Name = "listViewAvailableSkins";
      this.listViewAvailableSkins.Size = new System.Drawing.Size(440, 96);
      this.listViewAvailableSkins.TabIndex = 0;
      this.listViewAvailableSkins.UseCompatibleStateImageBehavior = false;
      this.listViewAvailableSkins.View = System.Windows.Forms.View.Details;
      this.listViewAvailableSkins.SelectedIndexChanged += new System.EventHandler(this.listViewAvailableSkins_SelectedIndexChanged);
      // 
      // colName
      // 
      this.colName.Text = "Name";
      this.colName.Width = 338;
      // 
      // colVersion
      // 
      this.colVersion.Text = "Version";
      this.colVersion.Width = 80;
      // 
      // previewPictureBox
      // 
      this.previewPictureBox.Anchor = System.Windows.Forms.AnchorStyles.None;
      this.previewPictureBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
      this.previewPictureBox.Location = new System.Drawing.Point(86, 144);
      this.previewPictureBox.Name = "previewPictureBox";
      this.previewPictureBox.Size = new System.Drawing.Size(300, 240);
      this.previewPictureBox.TabIndex = 2;
      this.previewPictureBox.TabStop = false;
      // 
      // Skin
      // 
      this.BackColor = System.Drawing.SystemColors.Control;
      this.Controls.Add(this.groupBox1);
      this.Name = "Skin";
      this.Size = new System.Drawing.Size(472, 408);
      this.groupBox1.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.previewPictureBox)).EndInit();
      this.ResumeLayout(false);

    }
    #endregion


    private void listViewAvailableSkins_SelectedIndexChanged(object sender, System.EventArgs e)
    {
      if (listViewAvailableSkins.SelectedItems.Count == 0)
      {
        previewPictureBox.Image = null;
        previewPictureBox.Visible = false;
        return;
      }
      string currentSkin = (string)listViewAvailableSkins.SelectedItems[0].Text;
      string previewFile = String.Format(@"{0}{1}\media\preview.png", SkinDirectory, currentSkin);

      //
      // Clear image
      //
      previewPictureBox.Image = null;
      System.Drawing.Image img = null;

      if (File.Exists(previewFile))
      {
        img = Image.FromFile(previewFile);
        previewPictureBox.Width = img.Width;
        previewPictureBox.Height = img.Height;
        previewPictureBox.Image = img;
        previewPictureBox.Visible = true;
      }
      else
      {
        string logoFile = "mplogo.gif";

        if (File.Exists(logoFile))
        {
          img = Image.FromFile(logoFile);
          previewPictureBox.Width = img.Width;
          previewPictureBox.Height = img.Height;
          previewPictureBox.Image = img;
          previewPictureBox.Visible = true;
        }
      }

    }
  }
}

