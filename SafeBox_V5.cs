using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading.Tasks; // NEW: For background processing
using System.Collections.Generic; // NEW: For filtering lists of files

namespace SafeBox
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && File.Exists(args[0]))
            {
                // Mode 2: Decrypt individual file passed via double-click
                Application.Run(new DecryptForm(args[0]));
            }
            else
            {
                // Mode 1: Bulk Encryption Manager UI
                Application.Run(new EncryptionManagerForm());
            }
        }
    }

    // --- ENCRYPTION MANAGER UI (MODIFIED) ---
    public class EncryptionManagerForm : Form
    {
        private TextBox txtFolder, txtPass, txtConfirm;
        private Button btnBrowse, btnEncrypt, btnDecrypt; // Added Decrypt button
        private ProgressBar progressBar; // Added Progress Bar
        private Label lblStatus; // Added Status Label

        public EncryptionManagerForm()
        {
            this.Text = "SafeBox Folder Manager V5";
            this.Size = new System.Drawing.Size(450, 310); // Made window taller
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            Label lblFolder = new Label() { Text = "Select Folder:", Left = 20, Top = 20, Width = 100 };
            txtFolder = new TextBox() { Left = 120, Top = 18, Width = 200, ReadOnly = true };
            btnBrowse = new Button() { Text = "Browse...", Left = 330, Top = 16, Width = 80 };

            Label lblPass = new Label() { Text = "Password:", Left = 20, Top = 60, Width = 100 };
            txtPass = new TextBox() { Left = 120, Top = 58, Width = 200, PasswordChar = '*' };

            Label lblConfirm = new Label() { Text = "Confirm:", Left = 20, Top = 100, Width = 100 };
            txtConfirm = new TextBox() { Left = 120, Top = 98, Width = 200, PasswordChar = '*' };

            btnEncrypt = new Button() { Text = "Encrypt Contents", Left = 120, Top = 135, Width = 200, Height = 30 };
            btnDecrypt = new Button() { Text = "Decrypt Contents", Left = 120, Top = 175, Width = 200, Height = 30 };

            progressBar = new ProgressBar() { Left = 20, Top = 220, Width = 390, Height = 15, Minimum = 0, Maximum = 100 };
            lblStatus = new Label() { Text = "Ready", Left = 20, Top = 240, Width = 390 };

            btnBrowse.Click += (s, e) => {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() == DialogResult.OK) txtFolder.Text = fbd.SelectedPath;
                }
            };

            // Hook up the buttons to our new smart processing method
            btnEncrypt.Click += async (s, e) => await ProcessFolderAsync(true);
            btnDecrypt.Click += async (s, e) => await ProcessFolderAsync(false);

            this.Controls.AddRange(new Control[] { lblFolder, txtFolder, btnBrowse, lblPass, txtPass, lblConfirm, txtConfirm, btnEncrypt, btnDecrypt, progressBar, lblStatus });
        }

        // NEW: Smart Background Processing Method
        private async Task ProcessFolderAsync(bool isEncrypting)
        {
            if (string.IsNullOrEmpty(txtFolder.Text) || !Directory.Exists(txtFolder.Text))
            {
                MessageBox.Show("Please select a valid folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(txtPass.Text) || txtPass.Text != txtConfirm.Text)
            {
                MessageBox.Show("Passwords do not match or are empty.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string folderPath = txtFolder.Text;
            string password = txtPass.Text;

            // Lock UI while working
            btnEncrypt.Enabled = false;
            btnDecrypt.Enabled = false;
            progressBar.Value = 0;
            lblStatus.Text = "Scanning folders...";

            int errorCount = 0; // NEW: Track how many files failed

            try
            {
                // Run heavy work on a background thread so UI doesn't freeze
                await Task.Run(() =>
                {
                    // 1. Get ALL files in folder AND subfolders
                    string[] allFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                    
                    // 2. Filter files based on what we are trying to do
                    List<string> targetFiles = new List<string>();
                    
                    foreach (string f in allFiles)
                {
                        if (isEncrypting && !f.EndsWith(".safe")) targetFiles.Add(f);
                        if (!isEncrypting && f.EndsWith(".safe")) targetFiles.Add(f);
                    }

                    int total = targetFiles.Count;
                    if (total == 0)
                    {
                        UpdateProgress(100, "No applicable files found to process.");
                        return;
                    }

                    // 3. Process the files
                    int processed = 0;
                    foreach (string file in targetFiles)
                    {
                        try
                        {
                            if (isEncrypting)
                            {
                                CryptoHelper.EncryptFile(file, password);
                            }
                            else
                            {
                                // Strip ".safe" from the end to get original filename
                                string outPath = file.Substring(0, file.Length - 5); 
                                CryptoHelper.DecryptFile(file, password, outPath);
                            }
                            File.Delete(file); 
                        }
                        catch (Exception innerEx)
                        {
                            // NEW: If ONE file fails, log it, count the error, and move to the next file!
                            Logger.LogError(innerEx.Message, file);
                            errorCount++;
                        }

                        processed++;

                        // Calculate percentage and update UI safely
                        int percent = (int)(((float)processed / total) * 100);
                        UpdateProgress(percent, "Processing: " + processed + " / " + total + " files");
                }
                });

                if (progressBar.Value == 100 && lblStatus.Text.StartsWith("Processing"))
                {
                    if (errorCount > 0)
                        MessageBox.Show("Finished, but " + errorCount + " files failed. Check SafeBox_ErrorLog.txt for details.", "Completed with Errors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else if (lblStatus.Text.StartsWith("Processing"))
                        MessageBox.Show("Operation completed successfully with 0 errors!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Critical Folder Process Failure: " + ex.Message);
                MessageBox.Show("A critical error occurred. Check the log.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Unlock UI
                btnEncrypt.Enabled = true;
                btnDecrypt.Enabled = true;
                txtPass.Clear(); 
                txtConfirm.Clear();
                if (progressBar.Value == 100) lblStatus.Text = "Done.";
            }
        }

        // Helper to safely update UI from the background thread
        private void UpdateProgress(int percent, string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateProgress(percent, text)));
                return;
            }
            progressBar.Value = percent;
            lblStatus.Text = text;
        }
    }
    
    // (LEAVE DecryptForm AND CryptoHelper CLASSES BELOW HERE EXACTLY AS THEY WERE)
    // --- DECRYPTION TRIGGER UI ---
    public class DecryptForm : Form
    {
        private string _encryptedFilePath;
        private TextBox txtPass;
        private Button btnOpen;

        public DecryptForm(string filePath)
        {
            _encryptedFilePath = filePath;
            this.Text = "SafeBox Unlock File V5";
            this.Size = new System.Drawing.Size(350, 150);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            Label lblPrompt = new Label() { Text = "Enter Password to Open File:", Left = 20, Top = 15, Width = 300 };
            txtPass = new TextBox() { Left = 20, Top = 40, Width = 290, PasswordChar = '*' };
            btnOpen = new Button() { Text = "Decrypt & Open", Left = 20, Top = 70, Width = 290, Height = 30 };

            btnOpen.Click += BtnOpen_Click;
            this.Controls.AddRange(new Control[] { lblPrompt, txtPass, btnOpen });
        }

        private void BtnOpen_Click(object sender, EventArgs e)
        {
            try
            {
                // 1. Setup a unique temp file path retaining original extension
                string originalCleanName = Path.GetFileNameWithoutExtension(_encryptedFilePath);
                string tempDir = Path.Combine(Path.GetTempPath(), "SafeBoxTemp");
                Directory.CreateDirectory(tempDir);
                string tempFilePath = Path.Combine(tempDir, originalCleanName);

                // 2. Attempt Decryption
                CryptoHelper.DecryptFile(_encryptedFilePath, txtPass.Text, tempFilePath);

                // 3. Hide this prompt window immediately
                this.Hide();
                
                // 4. Trigger standard file execution
                Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });

                // 5. Spin up a background thread to handle automatic cleanup silently
                System.Threading.Thread cleanupThread = new System.Threading.Thread(() =>
                {
                    // Give the target player/application 3 seconds to fully boot and catch the file
                    System.Threading.Thread.Sleep(3000);

                    // Loop every 2 seconds as long as the file is being read or held by Windows
                    while (IsFileLocked(tempFilePath))
                    {
                        System.Threading.Thread.Sleep(2000);
                    }

                    // Once the application releases it, scrub and delete it instantly
                    try
                    {
                        if (File.Exists(tempFilePath))
                        {
                            File.SetAttributes(tempFilePath, FileAttributes.Normal);
                            File.Delete(tempFilePath);
                        }
                    }
                    catch { /* Handle silent failures if OS is busy */ }

                    // Exit the entire background process cleanly
                    Environment.Exit(0);
                });

                cleanupThread.IsBackground = true;
                cleanupThread.Start();
            }
            catch (CryptographicException cx)
            {
                Logger.LogError("Failed to decrypt (Likely wrong password): " + cx.Message, _encryptedFilePath);
                MessageBox.Show("Incorrect password!", "Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
            catch (Exception ex)
            {
                Logger.LogError("Unexpected error opening file: " + ex.Message, _encryptedFilePath);
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        // Helper function to check if the unencrypted file is still open in another app
        private bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                // Try to open the file with exclusive access. 
                // If it fails, another app (player/viewer) is actively using it.
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true; // File is locked
            }
            return false; // File is completely free to be deleted
        }
    }

    // --- CRYPTO ENGINE (UNIVERSAL STANDARD: AES-CBC 256-bit, PBKDF2-SHA1 50000 Iterations) ---
    public static class CryptoHelper
    {
        private const int Iterations = 50000;

        public static void EncryptFile(string inputFile, string password)
        {
            string outputFile = inputFile + ".safe";
            byte[] salt = new byte[16];
            byte[] iv = new byte[16];

            // Generate secure random Salt and IV for every single file
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                using (var rfc = new Rfc2898DeriveBytes(password, salt, Iterations))
                {
                    aes.Key = rfc.GetBytes(32);
                    aes.IV = iv;
                }

                using (FileStream fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                {
                    // Prepend the Salt (16 bytes) and IV (16 bytes) to the file
                    fsOut.Write(salt, 0, salt.Length);
                    fsOut.Write(iv, 0, iv.Length);

                    using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    using (CryptoStream cs = new CryptoStream(fsOut, encryptor, CryptoStreamMode.Write))
                    using (FileStream fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                    {
                        fsIn.CopyTo(cs);
                    }
                }
            }
        }

        public static void DecryptFile(string inputFile, string password, string outputFile)
        {
            try
            {
                using (FileStream fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                {
                    byte[] salt = new byte[16];
                    byte[] iv = new byte[16];
                    
                    // Read the Salt and IV from the front of the file
                    fsIn.Read(salt, 0, 16);
                    fsIn.Read(iv, 0, 16);

                    using (Aes aes = Aes.Create())
                    {
                        aes.Mode = CipherMode.CBC;
                        using (var rfc = new Rfc2898DeriveBytes(password, salt, Iterations))
                        {
                            aes.Key = rfc.GetBytes(32);
                            aes.IV = iv;
                        }

                        using (FileStream fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                        using (ICryptoTransform decryptor = aes.CreateDecryptor())
                        using (CryptoStream cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
                        {
                            cs.CopyTo(fsOut);
                        }
                    }
                }
            }
            catch
            {
                // NEW: If decryption fails, the 'using' blocks above automatically close the file handles.
                // Now we step in and delete the corrupted/junk file that was left behind.
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
                
                throw; // Rethrow the error so the UI and Logger still know it failed
            }
        }
    }

    // --- ERROR LOGGING SYSTEM ---
    public static class Logger
    {
        public static void LogError(string message, string filePath = "System / Unknown")
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SafeBox_ErrorLog.txt");
                
                // FIXED: Removed the '$' and used standard string concatenation for C# 5 compatibility
                string logEntry = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] ERROR: " + message + " | File: " + filePath + Environment.NewLine;
                
                File.AppendAllText(logPath, logEntry);
            }
            catch 
            { 
                // Fail silently if we can't write the log (e.g., installed in a restricted Windows folder)
            }
        }
    }
}

// command to create the executable(.exe) file for windows : C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:SafeBox_V5.exe SafeBox_V5.cs