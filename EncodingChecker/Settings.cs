using System;
using System.Windows.Forms;

namespace EncodingChecker
{
    [Serializable]
    public sealed class Settings
    {
        public WindowPosition WindowPosition = new WindowPosition();

        public string BaseDirectory = Environment.CurrentDirectory;
        public bool IncludeSubdirectories = true;

        public string FileMasks;
    }

    [Serializable]
    public sealed class WindowPosition
    {
        public int Left = -1;
        public int Top = -1;
        public int Width = -1;
        public int Height = -1;
        public bool Maximized;

        public void ApplyTo(Form form)
        {
            if (Left >= 0 && Top >= 0 && Width > 0 && Height > 0)
                form.SetBounds(Left, Top, Width, Height);
        }
    }
}