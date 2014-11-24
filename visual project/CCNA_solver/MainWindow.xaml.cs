using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Data.SQLite;
using System.IO;
using System.Diagnostics;
using System.Data;
using System.Windows.Interop;
using WpfAutomation;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Threading;

namespace CCNA_solver
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SQLiteConnection _dbConnection;
        private HwndSource hWndSource;
        private IntPtr hWndNextViewer;
        private short atom;
        private short atom2;
        private bool isViewing;
        private String textToSearch;
        private String clipboard;
        private String databasePathAbsolute;
        private String databasePathRelative;

        public MainWindow()
        {
            InitializeComponent();
            Trace.Listeners.Add(new TextWriterTraceListener("program_log.txt"));
            Trace.AutoFlush = true;
            Trace.Indent();
            Trace.WriteLine("\n");
            clipboard = "";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("[" + DateTime.Now + "] [LOADING][INFO]: MaindWindows is starting");
            WindowInteropHelper wih = new WindowInteropHelper(this);
            hWndSource = HwndSource.FromHwnd(wih.Handle);
            hWndSource.AddHook(MainWindowProc);
            hWndNextViewer = Win32.SetClipboardViewer(hWndSource.Handle);   // set this window as a viewer 
            isViewing = true;
            
            // create an atom for registering the hotkey
            atom = Win32.GlobalAddAtom("Show");
            atom2 = Win32.GlobalAddAtom("Copy");

            if (atom == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            Trace.WriteLine("[" + DateTime.Now + "] [INIT][INFO]: WinApi init ended, registering keys");

            // register the Alt+C hotkey
            if (!Win32.RegisterHotKey(wih.Handle, atom2, Win32.MOD_ALT | Win32.MOD_SHIFT, 'S'))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            Trace.WriteLine("[" + DateTime.Now + "] [INIT][INFO]: Keys registered");
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Trace.WriteLine("[" + DateTime.Now + "] [CLOSE][INFO]: MainWindow closed.");
            Trace.Unindent();
            Trace.Flush();
        }

        private void addToTableButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                String sql = "INSERT INTO question (question_text, answers, good_answers) values ('" + richTextBoxString(questionTextBox) + "', '" + richTextBoxString(answersTextBox) + "', '" + richTextBoxString(goodAnswerTextBox) + "')";
                SQLiteCommand command = new SQLiteCommand(sql, _dbConnection);
                command.ExecuteNonQuery();
                questionTextBox.Document.Blocks.Clear();
                answersTextBox.Document.Blocks.Clear();
                goodAnswerTextBox.Document.Blocks.Clear();
                refreshTable();
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [INSERT][ERROR]: SQL exception has occured: " + ex.Message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [INSERT][ERROR]: Unexpected exception has occured: " + ex.Message);
            }
        }

        private void refreshTable()
        {
            try
            {
                string sql = "select * from question";
                SQLiteCommand command = new SQLiteCommand(sql, _dbConnection);
                SQLiteDataReader reader = command.ExecuteReader();
                DataTable dt = new DataTable();
                dt.Columns.Add("Selected", typeof(bool));
                dt.Columns[0].DefaultValue = false;
                dt.Columns.Add("question_id");
                dt.Columns[1].ReadOnly = true;
                dt.Columns.Add("question_text");
                dt.Columns.Add("answers");
                dt.Columns.Add("good_answers");
                while (reader.Read()) // Console.WriteLine("Name: " + reader["name"] + "\tScore: " + reader["score"]);
                {
                    DataRow dr = dt.NewRow();
                    dr["question_id"] = reader["question_id"];
                    dr["question_text"] = reader["question_text"];
                    dr["answers"] = reader["answers"];
                    dr["good_answers"] = reader["good_answers"].ToString().Replace("\r\n", "||");
                    dt.Rows.Add(dr);
                }
                dataGrid.ItemsSource = dt.DefaultView;
                reader.Close();
                dbRefresh();
            }
            catch (SQLiteException ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [READ][ERROR]: SQLite exception: " + ex.Message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [READ][ERROR]: Unexpected exception has occured: " + ex.Message);
            }
        }

        private void readTableButton_Click(object sender, RoutedEventArgs e)
        {
            refreshTable();
        }

        private IntPtr MainWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            
            switch (msg)
            {
                case Win32.WM_HOTKEY:
                    Trace.WriteLine("[" + DateTime.Now + "] [PROC][INFO]: Shoutcut pressed");
                    if ((int)wParam == atom)
                    {
                        modifyClipboard("hello");                        
                    }
                    else if((int)wParam == atom2)
                    {
                        this.Show();
                    }
                    handled = true;
                    break;
                case Win32.WM_DRAWCLIPBOARD:
                    if (this.Visibility == System.Windows.Visibility.Hidden)
                    {
                        Trace.WriteLine("[" + DateTime.Now + "] [PROC][INFO]: Clipboard changed.");
                        if (clipboard != Clipboard.GetText())
                        {
                            textToSearch = Clipboard.GetText();
                            clipboard = searchInDatabase(textToSearch, "question_text").Trim();
                            if (clipboard == "no result") clipboard = searchInDatabase(textToSearch, "answers").Trim();
                            if (clipboard != "no result") modifyClipboard(clipboard);
                        }
                    }
                    break; 
            }

            return IntPtr.Zero;
        }

        private void modifyClipboard(String data)
        {
            Clipboard.SetText(data);
            Trace.WriteLine("[" + DateTime.Now + "] [Clipboard][INFO]: Clipboard modified");
        }


        // TODO
        private String searchInDatabase(String text, String col)
        {
            int counter = 0;
            String answer = "##";
            String question = "";
            try
            {
                Trace.WriteLine("[" + DateTime.Now + "] [SEARCH][INFO]: Searching in database");
                string sql = "SELECT * FROM question WHERE " + col + " LIKE '%" + text + "%'";
                SQLiteCommand command = new SQLiteCommand(sql, _dbConnection);
                SQLiteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    answer = reader["good_answers"].ToString();
                    question += reader["question_text"].ToString() + "##";
                    counter++;
                }
                reader.Close();                
            }
            catch(SQLiteException ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [SEARCH][ERROR]: SQLite exception: " + ex.Message);
            }
            catch(Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [SEARCH][ERROR]: Unexpected exception has occured: " + ex.Message);
            }

            if (counter == 1) return answer;
            else if (counter == 0) return "no result";
            else return "Multiple result set, questions: " + question;
        }

        string richTextBoxString(RichTextBox rtb)
        {
            var textRange = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            return textRange.Text;
        }

        private void exitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void createMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog dialog = new SaveFileDialog();
                dialog.FileName = "database";
                dialog.DefaultExt = ".sqlite";
                dialog.Filter = "SQLite database files (.sqlite)| *.sqlite";

                if (dialog.ShowDialog() == true)
                {
                    databasePathAbsolute = dialog.FileName;
                    databasePathRelative = System.IO.Path.GetFileName(databasePathAbsolute);
                    SQLiteConnection.CreateFile(databasePathRelative); // Create DB file if it not exists
                    Trace.WriteLine("[" + DateTime.Now + "] [CREATE][INFO]: Creating new databse");
                    _dbConnection = new SQLiteConnection("Data Source=" + databasePathRelative + ";Version=3;"); // Connect to DB using connection string
                    _dbConnection.Open(); // Opening connection
                    Trace.WriteLine("[" + DateTime.Now + "] [CREATE][INFO]: Connected to database.");
                    String sql = "CREATE TABLE IF NOT EXISTS question (question_id INTEGER UNIQUE PRIMARY KEY AUTOINCREMENT NOT NULL, question_text LONGTEXT NOT NULL, answers LONGTEXT NOT NULL, good_answers LONGTEXT NOT NULL)";
                    SQLiteCommand command = new SQLiteCommand(sql, _dbConnection); // Command to execute
                    command.ExecuteNonQuery(); // Run that sql command
                }
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [CREATE][ERROR]: SQL exception has occured: " + ex.Message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [CREATE][ERROR]: Unexpected exception has occured: " + ex.Message);
            }
        }

        private void openMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.FileName = "database";
                dialog.DefaultExt = ".sqlite";
                dialog.Filter = "SQLite database files (.sqlite)| *.sqlite";

                if (dialog.ShowDialog() == true)
                {
                    databasePathAbsolute = dialog.FileName;
                    databasePathRelative = System.IO.Path.GetFileName(databasePathAbsolute);
                    _dbConnection = new SQLiteConnection("Data Source=" + databasePathRelative + ";Version=3;"); // Connect to DB using connection string
                    _dbConnection.Open(); // Opening connection
                    Trace.WriteLine("[" + DateTime.Now + "] [OPEN][INFO]: Connected to database.");
                    refreshTable();
                }
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [OPEN][ERROR]: SQL exception has occured: " + ex.Message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [OPEN][ERROR]: Unexpected exception has occured: " + ex.Message);
            }
        }

        private void hideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void showMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
        }

        private void dataGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            dataGrid.CommitEdit();
            DataRowView dataRow = (DataRowView)dataGrid.SelectedItem;
            int index = dataGrid.CurrentCell.Column.DisplayIndex;
            string cellValue = dataRow.Row.ItemArray[index].ToString();
            MessageBox.Show(cellValue);
        }


        private DataRowView rowBeingEdited = null;
        private void dataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            DataRowView rowView = e.Row.Item as DataRowView;
            rowBeingEdited = rowView;
        }

        private void dataGrid_CurrentCellChanged_1(object sender, EventArgs e)
        {
            if (rowBeingEdited != null)
            {
                try
                {
                    rowBeingEdited.EndEdit();
                    DataRowView dataRow = (DataRowView)dataGrid.SelectedItem;
                    int index = dataGrid.CurrentCell.Column.DisplayIndex;
                    String sql;
                    sql = "UPDATE question SET question_text='" + dataRow.Row.ItemArray[1].ToString() + "', answers='" + dataRow.Row.ItemArray[2].ToString() + "', good_answers='" + dataRow.Row.ItemArray[3].ToString() + "' WHERE question_id=" + dataRow.Row.ItemArray[0];
                    
                    SQLiteCommand command = new SQLiteCommand(sql, _dbConnection);
                    command.ExecuteNonQuery();
                }
                catch (System.Data.SQLite.SQLiteException ex)
                {
                    Trace.WriteLine("[" + DateTime.Now + "] [INSERT][ERROR]: SQL exception has occured: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[" + DateTime.Now + "] [INSERT][ERROR]: Unexpected exception has occured: " + ex.Message);
                }
                //MessageBox.Show(cellValue);
            }            
        }

        private void deleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (DataRowView item in dataGrid.Items)
                {
                    if ((bool)item.Row.ItemArray[0] == true)
                    {
                        String sql;
                        sql = "DELETE FROM question WHERE question_id=" + item.Row.ItemArray[1];

                        SQLiteCommand command = new SQLiteCommand(sql, _dbConnection);
                        command.ExecuteNonQuery();
                    }
                }

            }
            catch (Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [DELETE][ERROR]: Unexpected exception has occured: " + ex.Message);
            }
        }

        private void dbRefresh()
        {
            try
            {
                foreach (DataRowView item in dataGrid.Items)
                {
                    //MessageBox.Show(item.Row.ItemArray[0].ToString());
                    String sql;
                    sql = "UPDATE question SET question_text='" + item.Row.ItemArray[2].ToString() + "', answers='" + item.Row.ItemArray[3].ToString() + "', good_answers='" + item.Row.ItemArray[4].ToString() + "' WHERE question_id=" + item.Row.ItemArray[1];

                    SQLiteCommand command = new SQLiteCommand(sql, _dbConnection);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[" + DateTime.Now + "] [DELETE][ERROR]: Unexpected exception has occured: " + ex.Message);
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            
        }
    }
}


// TODO
// 1. ha a válaszra keresve több találat van, akkor az összes találat kérdését rakja vágólapra összefűzve