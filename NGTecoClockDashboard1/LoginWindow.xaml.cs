using System.Windows;

namespace NGTecoClockDashboard
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            txtUsername.Focus();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            // Basic validation
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                lblError.Text = "Please enter both username and password.";
                return;
            }

            // Authentication logic (Change these credentials as needed)
            if (username.Equals("admin", System.StringComparison.OrdinalIgnoreCase) && password == "admin123")
            {
                // Successful login
                DialogResult = true;
                Close();
            }
            else
            {
                lblError.Text = "Invalid username or password.";
                txtPassword.Clear();
                txtPassword.Focus();
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Application.Current.Shutdown();
        }
    }
}