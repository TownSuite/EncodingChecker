using System.Diagnostics;
using System.Windows.Forms;

namespace EncodingChecker
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void OnFormLoad(object sender, System.EventArgs e)
        {
            lblHomepage.Links[0].LinkData = "http://encodingchecker.codeplex.com";
            lblAuthor.Links[0].LinkData = "http://www.jeevanjames.com";
            lblLicense.Links[0].LinkData = "http://www.mozilla.org/MPL/MPL-1.1.html";
            lblCreditsUde.Links[0].LinkData = "http://code.google.com/p/ude/";
            lblCreditsCodePlex.Links[0].LinkData = "http://www.codeplex.com";
        }

        private void OnLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string url = (string)e.Link.LinkData;
            ProcessStartInfo startInfo = new ProcessStartInfo(url);
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }
    }
}