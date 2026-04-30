using System.Windows.Forms;

namespace EmailFilerv2
{
    public class BufferedListView : ListView
    {
        public BufferedListView()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
    ControlStyles.OptimizedDoubleBuffer |
    ControlStyles.AllPaintingInWmPaint,
    true
);

        }

    }
}
