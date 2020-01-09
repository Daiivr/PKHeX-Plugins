﻿using AutoModPlugins.GUI;
using System;
using System.Windows.Forms;

namespace AutoModPlugins
{
    public class SettingsEditor : AutoModPlugin
    {
        public override string Name => "Plugin Settings";
        public override int Priority => 3;

        protected override void AddPluginControl(ToolStripDropDownItem modmenu)
        {
            var ctrl = new ToolStripMenuItem(Name) { Image = Properties.Resources.legalizeboxes };
            ctrl.Click += SettingsForm;
            modmenu.DropDownItems.Add(ctrl);
        }

        private void SettingsForm(object sender, EventArgs e)
        {
            var settings = Properties.AutoLegality.Default;
            var form = new ALMSettings(settings);
            form.ShowDialog();
        }
    }
}

