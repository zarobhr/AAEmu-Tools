﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using AAPakEditor;
using AAEmu.Game.Utils.DB;
using FreeImageAPI;

namespace AAEmu.DBViewer
{
    public partial class MainForm : Form
    {
        private string defaultTitle;
        private AAPak pak = new AAPak("");

        class GameTranslation
        {
            public long idx = 0;
            public string table = string.Empty ;
            public string field = string.Empty ;
            public string value = string.Empty ;
        }
        Dictionary<string, GameTranslation> CurrentTranslations = new Dictionary<string, GameTranslation>();

        class GameItem
        {
            // Actual DB entries
            public long id = 0;
            public string name = string.Empty;
            public long catgegory_id = 1 ;
            public long level = 1 ;
            public string description = string.Empty;
            public long price = 0;
            public long refund = 0;
            public long max_stack_size = 1;
            public long icon_id = 1;
            public bool sellable = false;
            public long fixed_grade = -1;

            // Helpers
            public string nameLocalized = string.Empty;
            public string descriptionLocalized = string.Empty;
            public string SearchString = string.Empty;
        }
        Dictionary<long, GameItem> CurrentItems = new Dictionary<long, GameItem>();
        Dictionary<long, string> CurrentIcons = new Dictionary<long, string>();

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            defaultTitle = Text;
            if (!LoadServerDB(false))
            {
                Close();
                return;
            }
            string game_pakFileName = "C:\\ArcheAge\\Working\\game_pak";
            if (File.Exists(game_pakFileName))
            {
                pak.OpenPak(game_pakFileName, true);
            }
            tcViewer.SelectedTab = tpItems;
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (pak != null)
                pak.ClosePak();
        }

        private void LoadTableNames()
        {
            string sql = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name ASC";

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        lbTableNames.Items.Clear();
                        while (reader.Read())
                        {
                            lbTableNames.Items.Add(reader.GetString("name"));
                        }
                    }
                }
            }
        }

        private void LoadIcons()
        {
            string sql = "SELECT * FROM icons ORDER BY id ASC";

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        CurrentIcons.Clear();
                        while (reader.Read())
                        {
                            CurrentIcons.Add(reader.GetInt64("id"), reader.GetString("filename"));
                        }
                    }
                }
            }
        }

        private void LoadTranslations(string lng)
        {
            string sql = "SELECT id, tbl_name, tbl_column_name, idx, "+lng+ " FROM localized_texts ORDER BY tbl_name, tbl_column_name, idx";

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    CurrentTranslations.Clear();

                    command.CommandText = sql;
                    command.Prepare();
                    //command.Parameters.AddWithValue("@lng", lng);
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        Application.UseWaitCursor = true;
                        Cursor = Cursors.WaitCursor;

                        while (reader.Read())
                        {
                            GameTranslation t = new GameTranslation();
                            t.idx = reader.GetInt64("idx");
                            t.table = reader.GetString("tbl_name");
                            t.field = reader.GetString("tbl_column_name");
                            t.value = reader.GetString(lng);
                            string k = t.table + ":" + t.field + ":" + t.idx.ToString();
                            CurrentTranslations.Add(k, t);
                        }

                        Cursor = Cursors.Default;
                        Application.UseWaitCursor = false;

                    }
                }
            }
            Properties.Settings.Default.DefaultGameLanguage = lng ;
        }

        private void LoadItems()
        {
            string sql = "SELECT * FROM items ORDER BY id ASC";

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    CurrentItems.Clear();

                    command.CommandText = sql;
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        Application.UseWaitCursor = true;
                        Cursor = Cursors.WaitCursor;

                        while (reader.Read())
                        {
                            GameItem t = new GameItem();
                            t.id = reader.GetInt64("id");
                            t.name = reader.GetString("name");
                            t.catgegory_id = reader.GetInt64("category_id");
                            t.description = reader.GetString("description");
                            t.price = reader.GetInt64("price");
                            t.refund = reader.GetInt64("refund");
                            t.max_stack_size = reader.GetInt64("max_stack_size");
                            t.icon_id = reader.GetInt64("icon_id");
                            var sellableStr = reader.GetString("sellable");
                            t.sellable = (sellableStr.ToLower() == "t");
                            t.fixed_grade = reader.GetInt64("fixed_grade");

                            t.nameLocalized = GetTranslationByID(t.id, "items", "name", t.name);
                            t.descriptionLocalized = GetTranslationByID(t.id, "items", "description", t.descriptionLocalized);

                            t.SearchString = t.name + " " + t.description + " " + t.nameLocalized + " " + t.descriptionLocalized ;
                            t.SearchString = t.SearchString.ToLower();

                            CurrentItems.Add(t.id, t);
                        }

                        Cursor = Cursors.Default;
                        Application.UseWaitCursor = false;

                    }
                }
            }
            
        }


        private void BtnItemSearch_Click(object sender, EventArgs e)
        {
            dgvItem.Rows.Clear();
            string searchText = tItemSearch.Text ;
            if (searchText == string.Empty)
                return;
            string lng = cbItemSearchLanguage.Text;
            string sql = string.Empty;
            string sqlWhere = string.Empty;
            long searchID ;
            bool SearchByID = false;
            if (long.TryParse(searchText, out searchID))
                SearchByID = true;
            bool showFirst = true;
            long firstResult = -1;
            string searchTextLower = searchText.ToLower();

            // More Complex syntax with category names
            // SELECT t1.idx, t1.ru, t1.ru_ver, t2.ID, t2.category_id, t3.name, t4.en_us FROM localized_texts as t1 LEFT JOIN items as t2 ON (t1.idx = t2.ID) LEFT JOIN item_categories as t3 ON (t2.category_id = t3.ID) LEFT JOIN localized_texts as t4 ON ((t4.idx = t3.ID) AND (t4.tbl_name = 'item_categories') AND (t4.tbl_column_name = 'name') ) WHERE (t1.tbl_name = 'items') AND (t1.tbl_column_name = 'name') AND (t1.ru LIKE '%Камень%') ORDER BY t1.ru ASC 

            Application.UseWaitCursor = true;
            Cursor = Cursors.WaitCursor;
            dgvItem.Visible = false;
            foreach (var item in CurrentItems)
            {
                bool addThis = false;
                if (SearchByID)
                {
                    if (item.Key == searchID)
                    {
                        addThis = true;
                    }
                }
                else
                if (item.Value.SearchString.IndexOf(searchTextLower) >= 0)
                    addThis = true;

                if (addThis)
                {
                    int line = dgvItem.Rows.Add();
                    var row = dgvItem.Rows[line];
                    long itemIdx = item.Value.id;
                    if (firstResult < 0)
                        firstResult = itemIdx;
                    row.Cells[0].Value = itemIdx.ToString();
                    row.Cells[1].Value = item.Value.nameLocalized;

                    if (showFirst)
                    {
                        showFirst = false;
                        ShowDBItem(itemIdx);
                    }
                }
            }
            dgvItem.Visible = true;
            Cursor = Cursors.Default;
            Application.UseWaitCursor = false;

            if (firstResult >= 0)
                ShowDBItem(firstResult);

        }

        private string GetTranslationByID(long idx, string table, string field, string defaultValue = "")
        {
            string k = table + ":" + field + ":" + idx.ToString();
            if (CurrentTranslations.TryGetValue(k, out GameTranslation val))
                return val.value;
            // If no translation found ...
            if (defaultValue == "")
                return "<NT:" + table + ":" + field + ":" + idx.ToString() + ">";
            else
                return defaultValue;
        }

        /// <summary>
        /// Convert a hex string to a .NET Color object.
        /// </summary>
        /// <param name="hexColor">a hex string: "FFFFFFFF", "00000000"</param>
        public static Color HexStringToARGBColor(string hexARGB)
        {
            string a ;
            string r ;
            string g ;
            string b ;
            if (hexARGB.Length == 8)
            {
                a = hexARGB.Substring(0, 2);
                r = hexARGB.Substring(2, 2);
                g = hexARGB.Substring(4, 2);
                b = hexARGB.Substring(6, 2);
            }
            else
            if (hexARGB.Length == 6)
            {
                a = hexARGB.Substring(0, 2);
                r = hexARGB.Substring(2, 2);
                g = hexARGB.Substring(4, 2);
                b = hexARGB.Substring(6, 2);
            }
            else
            {
                // you can choose whether to throw an exception
                //throw new ArgumentException("hexColor is not exactly 6 digits.");
                return Color.Empty;
            }
            Color color = Color.Empty;
            try
            {
                int ai = Int32.Parse(a, System.Globalization.NumberStyles.HexNumber);
                int ri = Int32.Parse(r, System.Globalization.NumberStyles.HexNumber);
                int gi = Int32.Parse(g, System.Globalization.NumberStyles.HexNumber);
                int bi = Int32.Parse(b, System.Globalization.NumberStyles.HexNumber);
                color = Color.FromArgb(ai, ri, gi, bi);
            }
            catch
            {
                // you can choose whether to throw an exception
                //throw new ArgumentException("Conversion failed.");
                return Color.Empty;
            }
            return color;
        }

        private void FormattedTextToRichtEdit(string formattedText, RichTextBox rt)
        {
            rt.Text = string.Empty;
            rt.SelectionColor = rt.ForeColor;
            rt.SelectionBackColor = rt.BackColor;
            string restText = formattedText;
            restText = restText.Replace("\r\n\r\n", "\r");
            restText = restText.Replace("\r\n", "\r");
            restText = restText.Replace("\n", "\r");
            var colStartPos = -1;
            var colResetPos = -1;
            bool invalidPipe = false;
            while (restText.Length > 0)
            {
                var pipeStart = restText.IndexOf("|");

                if ((pipeStart >= 0) && (restText.Length >= pipeStart + 1))
                {
                    var pipeOption = restText.Substring(pipeStart + 1, 1);
                    colStartPos = -1;
                    colResetPos = -1;
                    invalidPipe = false;
                    switch (pipeOption)
                    {
                        case "c": if (restText.Length >= pipeStart + 10)
                                colStartPos = pipeStart;
                            break;
                        case "r": if  (restText.Length >= pipeStart + 1)
                                colResetPos = pipeStart;
                            break;
                        default:
                            invalidPipe = true;
                            break;
                    }
                }
                else
                {
                    colStartPos = -1;
                    colResetPos = -1;
                }

                if ((invalidPipe) && (pipeStart >= 0))
                {
                    // We need to handle this in order to fix bugs related to text formatting
                    rt.AppendText(restText.Substring(0, pipeStart));
                    rt.SelectionColor = rt.ForeColor;
                    rt.AppendText("|");
                    restText = restText.Substring(pipeStart + 1);
                }
                else
                if ((colStartPos >= 0) && (restText.Length >= colStartPos + 10))
                {
                    var colText = restText.Substring(colStartPos + 2, 8);
                    rt.AppendText(restText.Substring(0, colStartPos));
                    var newCol = HexStringToARGBColor(colText);
                    rt.SelectionColor = newCol;
                    restText = restText.Substring(colStartPos + 10);
                }
                else
                if ((colResetPos >= 0) && (restText.Length >= colResetPos + 2))
                {
                    rt.AppendText(restText.Substring(0, colResetPos));
                    rt.SelectionColor = rt.ForeColor;
                    restText = restText.Substring(colResetPos + 2);
                }
                else
                {
                    rt.AppendText(restText);
                    restText = string.Empty;
                }
            }
            
        }

        private void ShowDBItem(long idx)
        {
            if (CurrentItems.TryGetValue(idx,out var item))
            {
                lItemID.Text = idx.ToString();
                lItemName.Text = item.nameLocalized ;
                lItemCategory.Text = GetTranslationByID(item.catgegory_id, "item_categories", "name") + " (" + item.catgegory_id.ToString() + ")";
                FormattedTextToRichtEdit(item.descriptionLocalized,rtItemDesc);
                lItemLevel.Text = item.level.ToString();
                if (pak.isOpen)
                {
                    if (CurrentIcons.TryGetValue(item.icon_id, out var iconname))
                    {
                        var fn = "game/ui/icon/" + iconname;

                        if (pak.FileExists(fn))
                        {
                            try
                            {
                                var fStream = pak.ExportFileAsStream(fn);
                                var fif = FREE_IMAGE_FORMAT.FIF_DDS;
                                FIBITMAP fiBitmap = FreeImage.LoadFromStream(fStream, ref fif);
                                var bmp = FreeImage.GetBitmap(fiBitmap);
                                itemIcon.Image = bmp;

                                itemIcon.Text = "";
                                // itemIcon.Text = "[" + iconname + "]";
                            }
                            catch
                            {
                                itemIcon.Text = "ERROR - " + iconname ;
                            }
                        }
                        else
                        {
                            itemIcon.Text = "NOT FOUND - " + iconname + " ?";
                        }
                    }
                    else
                    {
                        itemIcon.Text = item.icon_id.ToString()+"?";
                    }
                }
                else
                {
                    itemIcon.Text = item.icon_id.ToString();
                }
            }
            else
            {
                lItemID.Text = idx.ToString();
                lItemName.Text = "<not found>";
                lItemCategory.Text = "" ;
                rtItemDesc.Clear();
                lItemLevel.Text = "";
                itemIcon.Text = "???";
            }
        }

        private string VisualizeDropRate(long droprate)
        {
            if (droprate == 1)
                return "1 (Always?)";
            double d = droprate / 100000;
            return d.ToString("0.00") + " %";
        }

        private void ShowDBLootByItem(long idx)
        {
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM loots WHERE (item_id = @tidx) ORDER BY id ASC";
                    command.Prepare();
                    command.Parameters.AddWithValue("@tidx", idx.ToString());
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        dgvLoot.Rows.Clear();
                        while (reader.Read())
                        {
                            if (reader.GetInt64("item_id") == idx)
                            {
                                int line = dgvLoot.Rows.Add();
                                var row = dgvLoot.Rows[line];

                                row.Cells[0].Value = reader.GetInt64("id").ToString();
                                row.Cells[1].Value = reader.GetInt64("loot_pack_id").ToString();
                                row.Cells[2].Value = idx.ToString();
                                row.Cells[3].Value = GetTranslationByID(idx, "items", "name");
                                row.Cells[4].Value = VisualizeDropRate(reader.GetInt64("drop_rate"));
                                row.Cells[5].Value = reader.GetInt64("min_amount").ToString();
                                row.Cells[6].Value = reader.GetInt64("max_amount").ToString();
                                row.Cells[7].Value = reader.GetInt64("grade_id").ToString();
                                row.Cells[8].Value = reader.GetString("always_drop").ToString();
                                row.Cells[9].Value = reader.GetInt64("group").ToString();
                            }
                        }
                    }
                }
            }
        }

        private void ShowDBLootByID(long loot_id)
        {
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM loots WHERE (loot_pack_id = @tpackid) ORDER BY id ASC";
                    command.Prepare();
                    command.Parameters.AddWithValue("@tpackid", loot_id.ToString());
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        dgvLoot.Rows.Clear();
                        Application.UseWaitCursor = true;
                        Cursor = Cursors.WaitCursor;
                        dgvLoot.Visible = false;
                        while (reader.Read())
                        {
                            if (reader.GetInt64("loot_pack_id") == loot_id)
                            {
                                int line = dgvLoot.Rows.Add();
                                var row = dgvLoot.Rows[line];

                                var itemid = reader.GetInt64("item_id");
                                row.Cells[0].Value = reader.GetInt64("id").ToString();
                                row.Cells[1].Value = reader.GetInt64("loot_pack_id").ToString();
                                row.Cells[2].Value = itemid.ToString();
                                row.Cells[3].Value = GetTranslationByID(itemid, "items", "name");
                                row.Cells[4].Value = VisualizeDropRate(reader.GetInt64("drop_rate"));
                                row.Cells[5].Value = reader.GetInt64("min_amount").ToString();
                                row.Cells[6].Value = reader.GetInt64("max_amount").ToString();
                                row.Cells[7].Value = reader.GetInt64("grade_id").ToString();
                                row.Cells[8].Value = reader.GetString("always_drop").ToString();
                                row.Cells[9].Value = reader.GetInt64("group").ToString();
                            }
                        }
                        dgvLoot.Visible = true;
                        Cursor = Cursors.Default;
                        Application.UseWaitCursor = false;
                    }
                }
            }
        }




        private void TItemSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                BtnItemSearch_Click(null,null);
            }
        }

        private void DgvItemSearch_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvItem.SelectedRows.Count <= 0)
                return;
            var row = dgvItem.SelectedRows[0];
            if (row.Cells.Count <= 0)
                return;

            var val = row.Cells[0].Value;
            if (val == null)
                return;
            ShowDBItem(long.Parse(val.ToString()));
        }

        private void CbItemSearchLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbItemSearchLanguage.Text != Properties.Settings.Default.DefaultGameLanguage)
            {
                Properties.Settings.Default.DefaultGameLanguage = cbItemSearchLanguage.Text;
                LoadServerDB(false);
            }
        }

        private void BtnOpenServerDB_Click(object sender, EventArgs e)
        {
            LoadServerDB(true);
        }

        private bool LoadServerDB(bool forceDlg)
        {
            string sqlfile = Properties.Settings.Default.DBFileName;

            while ( forceDlg || (!File.Exists(sqlfile)) )
            {
                forceDlg = false;
                if (openDBDlg.ShowDialog() != DialogResult.OK)
                {
                    return false;
                }
                else
                {
                    sqlfile = openDBDlg.FileName;
                }
            }

            SQLite.SQLiteFileName = sqlfile;
            Properties.Settings.Default.DBFileName = sqlfile;

            var i = cbItemSearchLanguage.Items.IndexOf(Properties.Settings.Default.DefaultGameLanguage);
            cbItemSearchLanguage.SelectedIndex = i;
            LoadTableNames();
            Text = defaultTitle + " - " + sqlfile + " ("+ Properties.Settings.Default.DefaultGameLanguage + ")";
            // make sure translations are loaded first, other tables depend on it
            LoadTranslations(Properties.Settings.Default.DefaultGameLanguage);
            LoadIcons();
            LoadItems();

            return true;
        }

        private void TItemSearch_TextChanged(object sender, EventArgs e)
        {
            btnItemSearch.Enabled = (tItemSearch.Text != string.Empty);
        }

        private void BtnFindItemInLoot_Click(object sender, EventArgs e)
        {
            if (dgvItem.SelectedRows.Count <= 0)
                return;
            var row = dgvItem.SelectedRows[0];
            if (row.Cells.Count <= 0)
                return;

            var val = row.Cells[0].Value;
            if (val == null)
                return;

            ShowDBLootByItem(long.Parse(val.ToString()));
            tcViewer.SelectedTab = tpLoot;
        }

        private void TLootSearch_TextChanged(object sender, EventArgs e)
        {
            btnLootSearch.Enabled = (tLootSearch.Text != string.Empty);
        }

        private void DgvLoot_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvLoot.SelectedRows.Count <= 0)
                return;
            var row = dgvLoot.SelectedRows[0];
            if (row.Cells.Count <= 0)
                return;

            var val = row.Cells[1].Value;
            if (val == null)
                return;
            tLootSearch.Text = val.ToString();
        }

        private void BtnLootSearch_Click(object sender, EventArgs e)
        {
            string searchText = tLootSearch.Text;
            if (searchText == string.Empty)
                return;
            long searchID;
            if (!long.TryParse(searchText, out searchID))
                return;

            ShowDBLootByID(searchID);
        }

        private void TLootSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                BtnLootSearch_Click(null, null);
            }
        }
    }
}
