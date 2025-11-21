using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace aSMP
{
    public partial class Form1 : Form
    {

        // TODO:
        // PUSH'lar aynı veriyi yükleyebiliyor, registerler tekrar kullanılabilir
        // çözüldü: başlangıç timing'i tutmuyor!!!      
        // çözüldü: Overrun olduğu zaman emülaston kesiliyor, oradaki register'ların sonraki raster'a doğru yansıdığından emin olalım
        // register dizme işi yanlış çalışıyor (gibi, tam hatırlamıyorum nasıl çalışması gerektiğini). adamshary2.mlt dosyasında görülüyor

        // görüntüyü yukarı aşağı sağa sola kaydırma seçeneği lazım

        // v71 Balance Rasters hala wip


        // v0.62 - pixel+paint mode eklendi, line mlt göstergeleri eklendi, satırdaki mlt sayısı bilgisi eklendi 12.kasım.24
        // v0.7 - Loop waitler eklendi. Dosyalar 6-8kb boyutunda 20/11/2024
        // v0.71 - Replace color, mlt swap mlt, basic düzgün restore ediyor 19.02.2025


        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private PictureBox pictSpare;
        // some constants

        string dosyam = ""; //"C:\\Users\\beyu\\Documents\\Visual Studio 2008\\Projects\\aSMP\\ddragonbigo.mlt";
        //string dosyam = "C:\\Users\\beyu\\Documents\\Visual Studio 2008\\Projects\\aSMP\\schx.mlt";
        Bitmap buffy = new Bitmap(512, 384);
        Bitmap palet = new Bitmap(128, 32);
        Bitmap palet2 = new Bitmap(36, 33);
        Color[] rg = new Color[18];
        SolidBrush curcol = new SolidBrush(Color.Red);
        Brush ula1 = Brushes.Magenta;
        Brush ula2 = Brushes.LawnGreen;
        Color mltcol = Color.Orange;
        int UIalpha = 160;

        bool optShowMLTadresses = true;
        
        string compressedbitmap = "";
        int rasterst = 14111; //start of program
        int contst = 14335; // start of contention
        int contend = 57248; //end of contention
        int modelts = 224;
        int warnthreshold = 4;
        string contpattern = "65432100";
        string[,] contmodel = { { "48k", "14335", "65432100", "224", "nop\r\nadd a,0\r\n" }, { "128k", "14361", "65432100", "228", "ld a,(32768)\r\nld bc,0\r\nld bc,0\r\n" }, { "+2a/b/3", "14361", "10765432", "228", "ld a,(32768)\r\nld bc,0\r\nld bc,0\r\n" } };
        string delayloop = "nop\r\nadd a,0\r\n";
        int[] cpat = { 6, 5, 4, 3, 2, 1, 0, 0 };
        string[] codes = new string[192];
        bool emulationactive = false; //triggers emulation mode
        cpu reg; //cpu registers and register queue.
        cpu[] inout = new cpu[192]; // in snapshot for every raster.
        string[] rlist = { "h", "l", "d", "e", "b", "c", "a", "i", "sp", "ix", "iy", "r" };//11 (0-10) registers supported

        //arrays
        int[,] mlt = new int[32, 192]; //holds multicolour data (8 per clash)
        int[,] umlt = new int[32, 192]; //holds spare(toggle) buffer for multicolour data (8 per clash)
        int[,] emlt = new int[32, 192]; //holds multicolour data for emulation mode(8 per clash)
        
        byte[,] sparebmp = new byte[32, 192]; //holds bitmap data for spare screen(sorted)
        byte[,] bmp = new byte[32, 192]; //holds bitmap data (sorted)
        byte[,] ubmp = new byte[32, 192]; //holds bitmap data (sorted) undo buffer (uset with umlt colour information)
        byte[] tape = new byte[6144]; //holds raw bitmap tape data (unsorted-screen native)

        int[,] spare = new int[32, 24]; //holds spare standard screen!
        int[, ,] attr = new int[32, 192, 10];//holds standard attributes
        //index 9 holds toggle info -> 0=spare, 1=mlt

        bool[,] clash = new bool[32, 24]; //signifies if a clash has multicolour or not. TRUE if it's a standard block, FALSE signifies a mlt block.
        int[,] chex = new int[32, 192];  //holds bytes to update in every interrupt. Creates rainbow effect.
        int[,] uchex = new int[32, 192]; //holds bytes to update in every interrupt. Creates rainbow effect.
        
        //Code generator:
        cpu[] buffer = new cpu[193]; //holds registers at the end of line.

        int density = 8; //zoom
        int ch = 1, cw = 8, cx = 1, cy = 1; //zoom
        int lx = 0, ly = 0, pixellx = 0; //old mouse position so we don't have to redraw always
        int ink = 0, paper = 7, bright = 0;
        int rgi = 0, rgp = 7, rgm = 56, pixelx = 0, pixely = 0, px = 0, py = 0, qptr = 0;
        int[,] safecycle = new int[32, 192]; //a pre-calc table that holds when ula beam scans a coordinate. An attribute must be updated before this cycle.

        int BalanceThreshold = 8; //raster limit for balancing.

        //ULA Emulator:
        int[] fts = new int[192];//Free T-states per raster
        int[] fazla = new int[193]; //holds excess tstate for this raster line
        int[] gmax = new int[192]; //holds maximum gmroups per raster
        int[] bmax = new int[192]; //holds maximum blocks per raster
        int[] umax = new int[192]; //holds maximum unique color data to write 
        int[] totmlt = new int[192]; //holds total mlt block count per raster
        int[, ,] groups = new int[192, 17, 7]; //holds group data per raster
        string[,] groupcode = new string[192, 17]; //holds prepared load code listings per group
        string[,] groupwcode = new string[192, 17]; //holds prepared load code listings per group
        string[] info = new string[192]; //Holds group info as human readable format
        string[,] codearray = new string[192, 255]; //holds prepared code listings per raster
        // 0.mnemonic, 1. size, 2.opcode as space seperated hex, 3.tstates, 4.flags, 5.description
        int cycle = 0;//timing emulator shared variable. Holds machine state!
        int lastulats = 0;
        int[] ram = new int[65535]; // emulated ram
        int emuCurrentRaster=0; //verbose emulating raster
        int codeLineIndex = 0; //when emulating, last processed line in code text file

        bool lastcomputedmarks = false;

        
        bool optDraggingNow = false;
        bool isHeldDown = false;


        // recent files management
        private List<string> recentFiles = new List<string>();
        private const string recentFilePath = "config.ini"; 
        private const int maxRecent = 10; 


        struct cpu
        {
            //possibly not used until full emulation takes place
            public int h;
            public int l;
            public int d;
            public int e;
            public int b;
            public int c;
            public int hx;
            public int lx;
            public int dx;
            public int ex;
            public int bx;
            public int cx;
            public int a;
            //public int f;
            public int i;
            public int ix;
            public int iy;
            public int r;
            public int sp;
            public int pc;
            public int cycle;


        }

        //will be used temporarily
        int[] rq = new int[17]; //register load queue
        int[] spq = new int[17]; //stack pointer queue
        int[,] q = new int[32, 2]; //single byte write queue [,0] adr, [,1] value
        int[] pql = new int[17]; //push queue length (always meaningful when used with spq)


        public Form1()
        {
            InitializeComponent();


        }

        // SCREEN UPDATE -------------------------------------------------------------------------------------

        private void setcell(Graphics G, int x, int y,int pokeColor=0)
        {
            Pen Golden=new Pen(Color.FromArgb( UIalpha,212,175,55));
            Pen Purpen=new Pen(Color.FromArgb( UIalpha,156,81,182));

            int gap = Convert.ToInt32(textBox5.Text);
            //Clear cell contents to default
            int[] rx = new int[3];
            int cols;
            if (emulationactive || lastcomputedmarks)
            {
                rx = getattr((byte)emlt[x, y]);
                cols = emlt[x, y];
                if (checkMARK.Checked)
                {
                    if (mlt[x, y] != emlt[x, y])
                    {
                        rx[0] = 16;
                        rx[1] = 16;
                        rx[2] = 0;
                        if (checkSTOP.Checked) emulationactive = false;

                    }

                }

            }
            else
            {
                rx = getattr((byte)mlt[x, y]);
                cols = mlt[x, y];
            }
            if (pokeColor == 1)
            {
                int uy = (cycle - contst) / modelts;
                int ux = ((cycle - (contst + (uy * modelts))) / 4);

                if ((uy >= y))
                {
                    G.FillRectangle(new SolidBrush(rg[16]), (x * cw) + gap, ((uy+1) * ch) + gap, cw - gap, (ch) - gap);
                }
                else
                {
                    G.FillRectangle(new SolidBrush(rg[17]), (x * cw) + gap, ((uy + 1) * ch) + gap, cw - gap, (ch ) - gap);
                }
            }
            if (chkShowAttribs.Checked) G.FillRectangle(new SolidBrush(rg[rx[1]]), (x * cw) + gap, (y * ch) + gap, cw - gap, ch - gap);
            if (checkBox2.Checked)
            {
                //put set bits
                //

                switch (cols)
                {

                    case 255:

                        G.FillRectangle(new SolidBrush(rg[rx[0]]), (x * cw) + gap, (y * ch) + 1, cw - gap, ch - gap);

                        break;
                    default:

                        for (byte z = 0; z <= 7; z++)
                        {
                            byte r = (byte)Math.Pow(2, z);

                            if ((bmp[x, y] & r) == r)
                            {
                                G.FillRectangle(new SolidBrush(rg[rx[0]]), (x * cw) + gap + ((cw / 8) * (7 - z)), (y * ch) + gap, (cw / 8), ch - gap);

                            }


                        }

                        break;
                }

            }

            if (optShowMLTadresses)
            {
                //DRAW MLT'S
                if (chex[x, y] == 1)
                {
                    //G.FillRectangle(new SolidBrush(Color.Gold), (x * cw) + 1, (y * ch) + 1, cw / 4, ch / 2);
                    //G.FillRectangle(new SolidBrush(Color.Purple), (x * cw) + cw / 2, (y * ch) + 1, cw / 4, ch / 2);
                    //G.FillRectangle(new SolidBrush(rg[getpaper(mlt[x, y])]), (x * cw) + 1, (y * ch)+1, cw - 1, ch -1);
                    //G.DrawRectangle(new Pen(Color.Gold), (x * cw) + 1, (y * ch) + 1, cw - 1, ch/2);
                    //G.DrawRectangle(new Pen(Color.Purple), (x * cw) + 1, (y * ch) + 1, cw - 1, ch / 2);
                    G.DrawLine(Golden, (x * cw) + 1, (y * ch) + 1, ((1 + x) * cw) - 1, (y * ch) + 1);
                    G.DrawLine(Golden, (x * cw) + 1, (y * ch) + 1, (x * cw) + 1, ((1 + y) * ch) - 1);
                    G.DrawLine(Purpen, (x * cw) + 1, ((1 + y) * ch) - 1, ((1 + x) * cw) - 1, ((1 + y) * ch) - 1);
                    G.DrawLine(Purpen, ((1 + x) * cw) - 1, ((1 + y) * ch) - 1, ((1 + x) * cw) - 1, (y * ch) + 1);

                }
                else if (chex[x, y] == 3)
                {
                    G.FillRectangle(new SolidBrush(Color.Olive), (x * cw) + 1, (y * ch) + 1, cw - 1, ch / 2);
                }
            }
            //check ula
            drawula(G);


        }

        private void drawcursor(Graphics G, int x, int y)
        {
            //if (y>191) return;
            if (radioButton5.Checked)
            {
                //scr/mlt toggle mode
                // do not draw cursor! :D

            }
            else
            {
                

                //cursor additions:
                if (checkLINE.Checked)
                {
                    // outline the whole line
                    G.DrawRectangle(new Pen(Color.FromArgb(UIalpha, rg[16].R, rg[16].G, rg[16].B)), 0, y, cw * 32, ch - 1);

                    if (checkLINEMLT.Checked)
                    {
                        int ty = y / ch;
                        int lcnt = 0;
                        if (ty % 8 != 0)
                            for (int k = 0; k < 32; k++)
                            {
                                if (chex[k, ty] == 1)
                                {
                                    lcnt++;
                                    G.DrawRectangle(new Pen(Color.White), (k * cw) , (y) , cw , ch );
                                
                                }
                            }
                        textBox10.Text = "Mlts: "+lcnt;
                        if (ty % 8 == 0) textBox10.Text += " No limit at line " + ty; 
                    }
                }

                if (radioButton6.Checked)
                {
                    G.FillRectangle(curcol, pixelx + 1, (pixely * ch) + (ch / 2), (cw / 8), ch / 2); // 1x1 pixel cursor
                }
                else
                {
                    //G.FillRectangle(curcol, x + 1, y + (ch / 2), cw - 1, ch / 2); // 8x1 cursor
                    //G.DrawRectangle(new Pen(rg[16]), x + 1, y + 1, cw - 3, ch - 3); //Color.FromArgb(UIalpha, rg[16].R,rg[16].G,rg[16].B)
                    //G.DrawRectangle(new Pen(rg[0]), x + 2, y + 2, cw - 5, ch - 5);

                    G.DrawLine(new Pen(rg[16]), x, y, x , y+ch-2);
                    G.DrawLine(new Pen(rg[16]), x, y,  x+cw-1 , y);
                    G.DrawLine(new Pen(rg[17]), x+cw-1  , y+ch-2, x, y+ch-2);
                    G.DrawLine(new Pen(rg[17]),  x+cw-1  , y+ch-2, x+cw-1, y);



                }

            }
        }

        private void freerasters(Graphics G)
        {
            if (chkFreeRasters.Checked)
            {
                for (int y = 0; y < ch * 192; y = y + (ch*8))
                {
                    G.DrawRectangle(new Pen(Color.FromArgb(128,252, 179, 232)), 0, y, cw * 256, ch);
                }
            }

        }

        private void drawgrid(Graphics G)
        {
            // Predefine pens outside the loops
            Pen kir = new Pen(Color.FromArgb(UIalpha, 255, 0, 0));
            Pen altin = new Pen(Color.FromArgb(UIalpha, 200, 200, 0));
            Pen gri = new Pen(Color.FromArgb(UIalpha, 160, 128, 128));
            Pen mcl = new Pen(Color.FromArgb(UIalpha, mltcol.R, mltcol.G, mltcol.B));
            Pen whitePen = new Pen(Color.White); // Avoid creating a new Pen multiple times

            int gridHeight = ch * 192;
            int gridWidth = cw * 32;
            int blockHeight = ch * 8;

            bool check5 = checkBox5.Checked;
            bool check4 = checkBox4.Checked;
            bool check1 = checkBox1.Checked;
            bool check6 = checkBox6.Checked;

            // Emphasize MLT or standard 8x8 blocks
            if (check5 || check4 || check1)
            {
                for (int y = 0; y < gridHeight; y += blockHeight)
                {
                    int blockY = y / ch / 8;
                    for (int x = 0; x < gridWidth; x += cw)
                    {
                        int blockX = x / cw;

                        if (check4 && clash[blockX, blockY])
                        {
                            G.DrawRectangle(whitePen, x + 1, y + 1, cw - 2, blockHeight - 2);
                        }
                        else
                        {
                            if (check1)
                            {
                                    G.DrawRectangle(gri, x, y, cw, blockHeight);
                            }
                            if (check5 && !clash[blockX, blockY])
                            {
                                G.DrawRectangle(mcl, x + 1, y + 1, cw - 2, blockHeight - 2);
                            }
                        }
                    }
                }
            }

            // Draw warning boxes
            
            if (check6)
            {
                for (int y = 0; y < gridHeight; y += ch)
                {
                    for (int x = 0; x < gridWidth; x += cw)
                    {
                        if (fts[y / ch] < warnthreshold)
                        {
                                G.DrawRectangle(kir, x, y, cw, ch);
                        }
                        else 
                        {
                            if (check1) G.DrawRectangle(gri, x, y, cw, ch);
                        }
                    }
                }
            }

            // Dispose of pens to release resources
            kir.Dispose();
            gri.Dispose();
            mcl.Dispose();
            whitePen.Dispose();
        }


        private void drawgridOLD(Graphics G)
        {

            Pen kir=new Pen(Color.FromArgb(UIalpha, 255,0,0));
            Pen gri=new Pen(Color.FromArgb(UIalpha, 160,128,128));
            Pen mcl=new Pen(Color.FromArgb(UIalpha, mltcol.R,mltcol.G,mltcol.B));
   

            //emphasize mlt or standard 8x8 blocks
            if (checkBox5.Checked || checkBox4.Checked || checkBox1.Checked)
            {
                for (int y = 0; y < ch * 192; y = y + (ch * 8))
                {
                    for (int x = 0; x < cw * 32; x = x + cw)
                    {
                        if (checkBox4.Checked)
                        {
                           if (clash[x / cw, (y / ch) / 8])  G.DrawRectangle(new Pen(Color.White), x + 1, y + 1, cw - 2, (ch * 8) - 2);
                        }
                        else
                        {
                            if (checkBox1.Checked)
                            {
                                if (fts[y / ch] < warnthreshold)
                                {
                                    G.DrawRectangle(kir, x, y, cw, ch);
                                }
                                else
                                {
                                    G.DrawRectangle(gri, x, y, cw, ch * 8);
                                }
                            }
                            if (checkBox5.Checked && !(clash[x / cw, (y / ch) / 8]))
                            {
                                G.DrawRectangle(mcl, x + 1, y + 1, cw - 2, (ch * 8) - 2);
                            }
                        }
                    }
                }
            }

            
            // Draw warning boxes
            if (checkBox1.Checked)
            {
                for (int y = 0; y < ch * 192; y = y + ch)
                {
                    for (int x = 0; x < cw * 32; x = x + cw)
                    {
                        if (fts[y / ch] < warnthreshold)
                        {
                            G.DrawRectangle(kir, x, y, cw, ch);
                        }
                        else
                        {
                            if (checkBox6.Checked) G.DrawRectangle(gri, x, y, cw, ch);
                        }
                    }
                }
            } 
             

        }

        private void CleanCursor(Graphics G, int curs_raster )
        {
            int startY = Math.Max(0, curs_raster );
            startY = Math.Min(190, curs_raster);
            int endY = 1;
            
            G.FillRectangle(new SolidBrush(Color.Black), 0, startY*ch, 32*cw, (endY*ch));
            for (int y = startY; y <= startY+endY; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    setcell(G, x, y);
                }
            }
            

        }


        private void drawcells(Graphics G)
        {
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    setcell(G, x, y);
                }
            }
        }


        private void renderBitmap(Graphics G, int[,] mltArray, byte[,] bitmapArray)
        {
            int gap = Convert.ToInt32(textBox5.Text);
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    int[] rx = getattr((byte)mltArray[x, y]);
                    int cols = mltArray[x, y];

                        
                        switch (cols)
                        {
                            case 0:
                                G.FillRectangle(new SolidBrush(rg[rx[1]]), (x * cw) + gap, (y * ch) + gap, cw - gap, ch - gap);
                                break;

                            case 255:

                                G.FillRectangle(new SolidBrush(rg[rx[0]]), (x * cw) + gap, (y * ch) + 1, cw - gap, ch - gap);

                                break;
                            default:
                                G.FillRectangle(new SolidBrush(rg[rx[1]]), (x * cw) + gap, (y * ch) + gap, cw - gap, ch - gap);
                                for (byte z = 0; z <= 7; z++)
                                {
                                    byte r = (byte)Math.Pow(2, z);

                                    if ((bitmapArray[x, y] & r) == r)
                                    {
                                        G.FillRectangle(new SolidBrush(rg[rx[0]]), (x * cw) + gap + ((cw / 8) * (7 - z)), (y * ch) + gap, (cw / 8), ch - gap);

                                    }


                                }

                                break;
                        }

                    
                }
            }
        }



        /// <summary>
        /// Setus up main screen, re-draws EVERYTHING!
        /// </summary>
        private void setup()
        {

            cw = density * 8;
            ch = cw / 8;
            pictureBox1.Width = (32 * cw);
            pictureBox1.Height = (192 * ch);

            buffy = new Bitmap(32 * cw, 192 * ch);
            Graphics G = Graphics.FromImage(buffy);

            G.Clear(rg[0]);
            drawcells(G);
            drawula(G);
            drawgrid(G);
            if (chkFreeRasters.Checked) freerasters(G);
            pictureBox1.Image = buffy;
            G.Dispose();

            drawpalet();


        }

        private void ShowSwapBuffer()
        {
            pictSpare = new PictureBox
            {
                Size = pictureBox1.Size,
                Location = pictureBox1.Location
            };
            this.Controls.Add(pictSpare);

            Bitmap buffyx = new Bitmap(32 * cw, 192 * ch);
            using (Graphics G = Graphics.FromImage(buffyx))
            {
                renderBitmap(G, umlt, ubmp);
            }

            pictSpare.Image = buffyx;
            
            pictSpare.BringToFront();
            pictSpare.Show();
        }

        private void DisposeSwap()
        {
            if (pictSpare != null)
            {
                this.Controls.Remove(pictSpare);  // Remove from form
                if (pictSpare.Image != null)
                {
                    pictSpare.Image.Dispose();  // Dispose of the bitmap
                }

                
                pictSpare.Dispose();  // Dispose of the PictureBox
                pictSpare = null;  // Nullify reference
            }

        }
        private void setupclash(Graphics G, int x, int y)
        {
            //redraws all 8x8 cell
            //needs device context G
            y = (int)(y / 8);
            y = y * 8;
            for (int u = y; u <= y + 7; u++)
            {

                setcell(G, x, u);

            }


        }

        private void drawpalet()
        {
            
            Graphics P = Graphics.FromImage(palet);
            int r = 0;
            int[] crenk = new int[3];
            crenk = getattr((byte)rgm);

            for (int x = 0; x < 16 * 8; x = x + 16)
            {
                SolidBrush fgbrush = new SolidBrush(rg[r]);
                P.FillRectangle(fgbrush, x, 0, 16, (16));

                if (crenk[0] == r)
                {
                    fgbrush = new SolidBrush(Color.DodgerBlue);
                    P.FillRectangle(fgbrush, x+2, 6, 4, 4);
                }
                if (crenk[1] == r)
                {
                    fgbrush = new SolidBrush(Color.Purple);
                    P.FillRectangle(fgbrush, x + 10, 6, 4, 4);
                }

                r++;
            }
            for (int x = 0; x < 16 * 8; x = x + 16)
            {
                SolidBrush fgbrush = new SolidBrush(rg[r]);
                P.FillRectangle(fgbrush, x, 16, 16, 16);
                if (crenk[0] == r)
                {
                    fgbrush = new SolidBrush(Color.DodgerBlue);
                    P.FillRectangle(fgbrush, x+2 , 22, 4, 4);
                }
                if (crenk[1] == r)
                {
                    fgbrush = new SolidBrush(Color.Purple);
                    P.FillRectangle(fgbrush, x + 10, 22, 4, 4);
                }
                r++;
            }
            pictureBox2.Image = palet;
            P.Dispose();
        }

        private void setpalet(int m)
        {
            // draw palette
            Graphics P = Graphics.FromImage(palet2);


            int[] r = new int[3];
            r = getattr((byte)m);

            rgi = r[0];
            rgp = r[1];

            SolidBrush fgbrush = new SolidBrush(rg[rgi]);
            P.FillRectangle(fgbrush, 0, 0, 16, 16);

            fgbrush = new SolidBrush(rg[rgp]);

            P.FillRectangle(fgbrush, 16, 0, 17, 16);
            P.FillRectangle(Brushes.Black, 16, 0, 1, 16);

            //pictureBox3.Width = 33;
            //pictureBox3.Height = 33;
            pictureBox3.Image = palet2;

            P.Dispose();

        }

        private void showpalet()
        {
            // draw palette

            rgi = ink + (bright * 8);
            rgp = paper + (bright * 8);
            rgm = ink + paper * 8 + bright * 64;
            setpalet(rgm);
        }

        private void palethover(int x, int y)
        {
            // draw palette
            Graphics P = Graphics.FromImage(palet2);

            //P.Clear(Color.White);
            int[] r = new int[3];
            r = getattr((byte)mlt[x, y]);

            rgi = r[0];
            rgp = r[1];

            SolidBrush fgbrush = new SolidBrush(rg[rgi]);
            P.FillRectangle(fgbrush, 0, 16, 16, 16);

            fgbrush = new SolidBrush(rg[rgp]);

            P.FillRectangle(fgbrush, 16, 16, 17, 16);
            P.FillRectangle(Brushes.Black, 16, 16, 1, 16);

            //pictureBox3.Width = 33;
            //pictureBox3.Height = 33;
            pictureBox3.Image = palet2;

            P.Dispose();
        }

        private void drawula(Graphics G)
        {
            int y = (cycle - contst) / modelts;
            int x = ((cycle - (contst + (y * modelts))) / 4);
            if ((emulationactive && checkVERBOSE.Checked) || (!emulationactive))
            {
                if (checkULA.Checked)
                {
                    G.FillRectangle(ula1, (x * cw) + (cw / 5), (y * ch) + 1, 3 * (cw / 4), ch / 3);
                    G.FillRectangle(ula2, (x * cw) + (cw / 5), (y * ch) + (ch / 2), 3 * (cw / 4), ch / 3);
                }
            }

        }

        private void moveula(int Ts) //move ula raster until it reaches to ts. Using cycle as basis
        {
            if (!emulationactive) return; //no need to execute if we are not emulating!

            for (int k = reg.cycle; k < Ts; k++)
            {
                int c = contention(k);
                if ((c == 3) || (c == 1))
                {
                    //read cycle
                    int data = ram[cycletoaddress(k)];
                    int y = (k - contst) / modelts;
                    int x = ((k - (contst + (y * modelts))) / 4);
                    emlt[x, y] = data;
                    Graphics G = Graphics.FromImage(buffy);
                    cycle = Ts;
                    setcell(G, x, y);
                    pictureBox1.Image = buffy;
                    G.Dispose();
                }

            }

            if (checkVERBOSE.Checked) textBox8.Text = Ts.ToString();
        }

        private void updateregs()
        {
            textHL.Text = (reg.h * 256 + reg.l).ToString();
            textDE.Text = (reg.d * 256 + reg.e).ToString();
            textBC.Text = (reg.b * 256 + reg.c).ToString();
            textAF.Text = (reg.a).ToString();
            textSP.Text = reg.sp.ToString();


        }     //update register output when in verbose emulation 

        private void move_cursor(int mX, int mY, int mB) // move red cursor
        {

            if ((mX > ((cw * 32) - 1)) || (mY > ((ch * 192) - 1)) || (mY < 0) || (mX < 0)) return;

            mB = Convert.ToInt16((mB.ToString()).Substring(0, 1));

            int x = (int)(mX / cw) * cw; 
            int y = (int)(mY / ch) * ch;
            cx = x / cw;  //cursor cell position x,y
            cy = y / ch;
            pixelx = ((int)(mX / (cw / 8)) * (cw / 8));
            pixely = cy;
            palethover(cx, cy);
            if ((cx > 31) || (cy > 191)) return;


            textBox1.Text = "[" + cx + "x," + cy + "y] "+ " w"+x+" h"+y+" attr: " + mlt[cx, cy] + " (#" + dectohex(mlt[cx, cy]) + "), bitmap: " + bmp[cx, cy] + " (#" + dectohex(bmp[cx, cy]) + ")";
            int ca = cycletoaddress(safecycle[cx, cy]);
            textBox2.Text = "Cursor: " + safecycle[cx, cy].ToString() + " Start:" + safecycle[0, cy].ToString() + " Addr: " + ca.ToString() + " (#" + dectohex4(ca) + ")";
            textBox3.Text = fts[cy] + " Excess: " + fazla[cy + 1].ToString();
            if (fts[cy] < warnthreshold) { textBox3.BackColor = Color.Red; } else { textBox3.BackColor = Color.White; }
            Graphics G = Graphics.FromImage(buffy);
            if ((ly != cy) && (checkLINE.Checked))
            {
                //erase old line cursor
                //G.DrawRectangle(new Pen(Color.Black), 0, ly * ch, cw * 32, ch);
                CleanCursor(G, ly);


                drawgrid(G);
            }
            if (mB != 0)
            {
                // MB1------------------------------------------------------------------------------- Mouse Button Handle
                if (mB == 1)
                {

                    textBMP.Text = Convert.ToString(bmp[cx, cy], 2).PadLeft(8, '0');
                    label18.Text = cx + "," + cy;
                    txtDebugX.Text = cx.ToString();
                    txtDebugY.Text = cy.ToString();
                    txtDebugAddr.Text = ca.ToString();

                    if (!radioButton6.Checked && (!radioButton2.Checked))
                    {
                        if (radioButton7.Checked)
                        {
                            chex[cx, cy] = 1;
                        }
                        else if (radioButton5.Checked)
                        {
                            //toggle cell
                            togglelr(cx, cy / 8, 1);
                        }
                        else if (radioButton10.Checked)
                        {
                            int tx = (int)(px / cw) * cw; //round nearest
                            int ty = (int)(py / ch) * ch;
                            int pcx = tx / cw;  //cursor cell position x,y
                            int pcy = ty / ch;
                            if (isHeldDown && ((pcx != cx) || (pcy != cy))) //check if we are still at the same cell
                            {
                                px = cx * cw;
                                py = cy * ch;
                                toggleMultiSwap(cx, cy);
                            }
                            else if (!isHeldDown){
                                
                                toggleMultiSwap(cx, cy);
                            }
                        }
                        else
                        {
                            chex[cx, cy] = 1;
                            if (radioButton1.Checked)
                            {
                                //set colors only
                                mlt[cx, cy] = rgm;
                            }

                            if (radioToggleBri.Checked)
                            {
                                //toggle brighness
                                if (mlt[cx, cy] > 63) mlt[cx, cy] = mlt[cx, cy] - 64; else mlt[cx, cy] = mlt[cx, cy] + 64;
                            }
                            if (radioButton9.Checked)
                            {
                                if (cy > 0)
                                {
                                    //copy from above
                                    mlt[cx, cy] = mlt[cx, cy - 1];
                                }
                            }
                            if (radioButton8.Checked)
                            {
                                if (cy < 191)
                                {
                                    //copy from below
                                    //
                                    // //if shift is pressed
                                    if ((GetAsyncKeyState(0x10) & 0x8000) != 0) CopyFromBelowAndFix(cx, cy); else mlt[cx, cy] = mlt[cx, cy + 1];  // or try fixcopy else direct copy

                                }
                            }
                            if (radioButton3.Checked)
                            {
                                //pick color
                                rgm = mlt[cx, cy];
                                setpalet(rgm);
                                drawpalet();
                            }
                            else
                            {
                                if ((cy < 192) && (((cy + 1) % 8) != 0))
                                {
                                    if (chex[cx, cy + 1] == 0)
                                    {
                                        chex[cx, cy + 1] = 3;
                                        setcell(G, cx, cy + 1);
                                    }
                                }
                            }
                        }

                    }
                    else
                    {
                        //pixel mode
                        //setpixel
                        int bit = ((int)(pixelx / (cw / 8))) % 8;
                        bmp[cx, cy] |= ((byte)(1 << (7 - bit)));
                        
                        if (radioButton2.Checked)
                        {
                            mlt[cx, cy] = rgm;
                        }

                        setcell(G, cx, cy);
                    }

                }


                //MB2 -------------------------------------------------------------------------------
                if (mB == 2)
                {
                    if (radioButton7.Checked)
                    {
                        chex[cx, cy] = 0;
                    }
                    if (!radioButton6.Checked)
                    {
                        if (radioButton5.Checked)
                        {
                            //toggle cell
                            togglelr(cx, cy / 8, 2);
                        }
                        else if (radioButton10.Checked)
                        {
                            //togglelr(cx, cy, 2);
                        }
                        else
                        {
                            chex[cx, cy] = 0;
                            /*if (radioButton1.Checked)
                            {
                                mlt[cx, cy] = attr[cx, cy / 8, 8];
                            }*/
                            if ((cy < 192) && (((cy + 1) % 8) != 0))
                            {
                                if (chex[cx, cy + 1] == 3)
                                {
                                    chex[cx, cy + 1] = 0;
                                    setcell(G, cx, cy + 1);
                                }
                            }
                        }
                    }
                    else
                    {
                        //erase pixel
                        int bit = ((int)(pixelx / (cw / 8))) % 8;
                        bmp[cx, cy] &= ((byte)~(1 << (7 - bit)));
                        setcell(G, cx, cy);
                    }
                }

                //MB4 (middle mouse) -----------------------------------------------------------------------
                if (mB == 4)
                {
                    if (radioButton5.Checked )
                    {
                        //toggle cell
                        toggle(cx, cy / 8);
                    }
                    else if (radioButton10.Checked)
                    {
                        toggleMultiSwap(cx, cy);
                    }
                    else
                    {
                        attr[cx, cy / 8, 8] = rgp;
                    }
                }

                setcell(G, cx, cy);
                drawcursor(G, x, y);
                //processq (cy);

                if (radioButton7.Checked!=true) checkstandards(cx, cy);

                setupclash(G, cx, cy);

            }


            if (((pixellx != pixelx) && radioButton6.Checked) || ((lx != cx) && !radioButton6.Checked) || (ly != cy))
            {
                setcell(G, lx, ly);
                lx = cx;
                ly = cy;
                drawcursor(G, x, y);
                //textBox6.Text = info[cy];
                //textBox4.Text = codes[cy];
                comboBox4.SelectedIndex = cy; //cy+1 olacak
            }

            if (chkFreeRasters.Checked) freerasters(G);

            pictureBox1.Image = buffy;
            G.Dispose();
        }

        // TOOLS **********************************************************************************************

        private void resetmodel()
        {
            for (int y = 0; y < 192; y++)
            {

                fts[y] = modelts;

            }
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 192; y++)
                {
                    safecycle[x, y] = contst + (y * modelts) + ((x) * 4);

                }
            }

            buffer[0].a = 0;
            buffer[0].b = 0;
            buffer[0].c = 0;
            buffer[0].d = 0;
            buffer[0].e = 0;
            buffer[0].h = 0;
            buffer[0].l = 0;
            buffer[0].i = 254;
            buffer[0].ix = 0;
            buffer[0].iy = 0;
            buffer[0].r = 0;
            ram[32768] = 33;

        }

        private int putattr(int ink, int paper)
        {
            ink = (ink == 99) ? 0 : ink;
            paper = (paper == 99) ? 0 : paper;
            int bri = bright;
            if (ink > 7) { bri = 1; ink -= 8; }
            if (paper > 7) { bri = 1; paper -= 8; }

            int attr = (ink) + (paper * 8) + (bri * 64) + (0 * 128);

            return attr;
        }

        private int MakeAttrByte(int mink, int mpaper, int mbrigtness, int mflash = 0)
        {
            if (mink > 7) mink -= 8;
            if (mpaper > 7) mpaper -= 8;
            if (mbrigtness > 0) mbrigtness = 1; else mbrigtness = 0;

            int attr = (mink) + (mpaper * 8) + (mbrigtness * 64) + (mflash * 128);
            return attr;
        }

        

        private int[] getattr(byte data)
        {

            int[] renkler = new int[3];
            int ink;
            int paper;
            int bri = 0;
            int rng = data;

            if ((rng & 128) != 0) { rng -= 128; }
            if ((rng & 64) != 0) { rng -= 64; bri = 8; }
            paper = ((int)(rng / 8));
            ink = (rng - (paper * 8)) + bri;
            paper += bri;

            renkler[0] = ink;
            renkler[1] = paper;
            renkler[2] = bri;

            return renkler;
        }

        private int[] getattrwFlash(byte data)
        {

            int[] renkler = new int[4];
            int ink;
            int paper;
            int bri = 0;
            int fla = 0;
            int rng = data;

            if ((rng & 128) != 0) { rng -= 128; fla = 1; }
            if ((rng & 64) != 0) { rng -= 64; bri = 8; }
            paper = ((int)(rng / 8));
            ink = (rng - (paper * 8)) + bri;
            paper += bri;

            renkler[0] = ink;
            renkler[1] = paper;
            renkler[2] = bri;
            renkler[3] = fla;
            return renkler;
        }

        private int[] getattrignore(byte data)
        {

            int[] renkler = new int[3];
            int ink;
            int paper;
            int bri = 0;
            int rng = data;

            if ((rng & 128) != 0) { rng -= 128; }
            if ((rng & 64) != 0) { rng -= 64; bri = 8; }
            paper = ((int)(rng / 8));
            ink = (rng - (paper * 8)) + bri;
            paper += bri;
            if (ink == 8) ink = 0;
            if (paper == 8) paper = 0;
            renkler[0] = ink;
            renkler[1] = paper;
            renkler[2] = bri;

            return renkler;
        }

        private int getpaper(int data)
        {

            int paper;
            int bri = 0;
            int rng = data;

            if ((rng & 128) != 0) { rng -= 128; }
            if ((rng & 64) != 0) { rng -= 64; bri = 8; }
            paper = ((int)(rng / 8));
            paper += bri;

            return paper;
        }

        static void Swap<T>(ref T lhs, ref T rhs)
        {
            T temp;
            temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        private void exx()
        {

            Swap<int>(ref reg.h, ref reg.hx);
            Swap<int>(ref reg.l, ref reg.lx);
            Swap<int>(ref reg.d, ref reg.dx);
            Swap<int>(ref reg.e, ref reg.ex);
            Swap<int>(ref reg.b, ref reg.bx);
            Swap<int>(ref reg.c, ref reg.cx);

        }

        private void ld(string register, int value)
        {

            int hibyte = (int)((value % 65536) / 256);
            int lobyte = (value % 65536) % 256;

            switch (register)
            {
                case "hl":
                    reg.h = hibyte;
                    reg.l = lobyte;
                    break;

                case "de":
                    reg.d = hibyte;
                    reg.e = lobyte;
                    break;

                case "bc":
                    reg.b = hibyte;
                    reg.c = lobyte;
                    break;

                case "ix":
                    reg.ix = value;

                    break;

                case "iy":
                    reg.iy = value;
                    break;

            }

        }

        private int rd(string register)
        {

            switch (register)
            {
                case "hl":
                    return reg.h * 256 + reg.l;


                case "de":
                    return reg.d * 256 + reg.e;

                case "bc":
                    return reg.b * 256 + reg.c;

                case "ix":
                    return reg.ix;

                case "iy":
                    return reg.iy;

                case "a":
                    return reg.a;

                case "i":
                    return reg.i;

                case "h":
                    return reg.h;

                case "l":
                    return reg.l;

                case "b":
                    return reg.b;

                case "c":
                    return reg.c;

                case "d":
                    return reg.d;

                case "e":
                    return reg.e;

                case "sp":
                    return reg.sp;

                case "r":
                    return reg.r;

                default:
                    return 0;

            }
        }

        private int cycletoaddress(int c)
        {
            int y = (int)(0.5 + ((c - contst) / modelts));
            int x = ((c - contst) - (y * modelts)) / 4;
            if (x > 31) x = 31;
            y = y / 8;
            int a = 22528 + (x + (y * 32)); ;
            //if (a<22528) a=0;


            return a;
        }

        private int coordtoaddress(int x, int y)
        {
            int a = 22528;
            a += (y * 32) + x;

            return a;
        }

        private int mltcoordtoaddress(int x, int y)
        {
            int a = 22528;
            a += ((int)(y / 8)) * 32 + x;

            return a;
        }

        private int[] address_to_mltcoord(int address)
        {
            int baseAddress = 22528;

            // Calculate offset from the base address
            int offset = address - baseAddress;

            // Derive x and y
            int y = (offset / 32) * 8; // Calculate the row and scale back to pixels
            int x = offset % 32;       // Calculate the column

            return new int[] { x, y };
        }


        private int[] address_to_coord(int address)
        {
            int baseAddress = 22528;

            // Calculate offset from base address
            int offset = address - baseAddress;

            // Calculate x and y
                   // Column
            int y = offset / 32;       // Row
            int x = offset % 32;

            return new int[] { x, y };
        }

        private string coordtohex(int x, int y)
        {
            //get data
            string hex = String.Format("{0:X2}", mlt[x, y]);
            hex += String.Format("{0:X2}", mlt[x + 1, y]);
            return "#" + hex;
        }

        private string icoordtohex(int x, int y)
        {
            //inversed coord to hex (because stack writes backwars)
            //get data
            string hex = String.Format("{0:X2}", mlt[x + 1, y]);
            hex += String.Format("{0:X2}", mlt[x, y]);
            return "#" + hex;
        }

        private int coordtodec(int x, int y)
        {

            //get data
            return (mlt[x + 1, y] * 256) + mlt[x, y];
        }

        private int icoordtodec(int x, int y)
        {

            //inverted - get data
            int b = 0;
            if (x >= 0)
            {
                b = mlt[x + 1, y];
            }
            return (b * 256) + mlt[x + 1, y];
        }

        private string dectohex4(int dec)
        {

            return String.Format("{0:X4}", dec); ;   //result: hexValue = "ABCD"
        }

        private string dectohex(int dec)
        {

            return String.Format("{0:X2}", dec); ;   //result: hexValue = "ABCD"
        }

        public void addtoq(int adr, int value)
        {

            q[qptr, 0] = adr;
            q[qptr, 1] = value;
            qptr++;

        }

        private void invertq()
        {
            int[,] qb = new int[32, 2];
            Array.Copy(q, qb, q.Length);
            int bptr = qptr;
            qptr = 0;
            for (int x = bptr - 1; x >= 0; x--)
            {
                addtoq(qb[x, 0], qb[x, 1]);
            }
        }

        /// <summary>
        /// This tries to apply contention to an opcode
        /// opcode is actual asm line, ts is the actual cycle of machine
        /// function returns new ts, after appying contention
        /// </summary>
        /// <param name="opcode"></param>
        /// <param name="ts"></param>
        /// <returns></returns>
        private int applycontention(string opcode, int ts)
        {

            string[] u = opcode.Split(';');
            string[] s = u[0].Split(',');
            s[0] = s[0].Trim();
            if (s[0] != "") if (opcode.Substring(0, 3) == "djn") s[0] = "djnz";

            string[] r = s[0].Split(' ');
            switch (s[0])
            {
                case "res 3":
                case "set 3":
                    ts += 8;
                    break;

                case "out (c)": //pc:+4, pc+1:+4, io:+1,+1,+1,+1
                    //switching banks here
                    ts += 8;
                    ts += contention(ts);
                    ts++; //C:1
                    ts += contention(ts);
                    ts++; //C:1
                    ts += contention(ts);
                    ts++; //C:1
                    ts += contention(ts);
                    ts++; //C:1
                    break;

                case "jr nz":
                    ts += (reg.a - 1) * 16;
                    ts += 7;
                    reg.a = 0;
                    break;

                case "dec a":
                    //no decrementing a here. this is only used with jrnz.
                    ts += 4; 
                    break;

                case "ld b":
                    //always uncontended, 7ts
                    reg.b = Convert.ToInt32(s[1]);
                    ts += 7;
                    break;

                case "djnz":
                    //always uncontended, calculated based on reg.b
                    ts += 13 * (reg.b-1);
                    ts += 8;
                    break;

                case "ld hl":
                case "ld de":
                case "ld bc":
                case "ld sp":
                    //always uncontended, 10ts

                    ts += 10;

                    break;

                case "push hl":
                case "push de":
                case "push bc":
                    //PUSH RR= PC:4, IR:1, SP:3, SP:3
                    //delayed if sp in contended area. (will always be in contended area!)
                    ts += 4; //read pc
                    ts += 1;//ir contention=none
                    ts += contention(ts); //SP delay
                    ts += 3;
                    ts += contention(ts); //SP2 delay
                    ts += 3;

                    break;

                case "ld (hl)":
                    // LD(hl),n= PC:4, PC+1=3, hl=3
                    ts += 4; //pc:4 read pc
                    s[1] = s[1].Trim();
                    if (!Regex.IsMatch(s[1], @"^[a-zA-Z]+$"))
                    {
                        ts += 3; //pc:3 read n
                    }
                    ts += contention(ts); //apply contention
                    ts += 3; //hl:3

                    break;

                case "exx":
                    ts += 4; //pc:4 read pc not contended
                    break;

                case "nop":

                    ts += 4; //pc:4 read pc not contended
                    break;

                case "inc hl":
                    ts += 6;
                    break;

                case "add a":
                    ts += 7;

                    break;

                case "ld r":
                    ts += 8;
                    break;

                case "ld a":
                    
                    s[1] = s[1].Trim();
                    if (s[1].Substring(0,1) != "(")
                    {
                        //ld a,n
                        reg.a = Convert.ToInt32(s[1]);
                        ts += 7;
                    }
                    else
                    {
                        //ld a,(NN)

                        //pc:4,pc+1:3,pc+2:3,nn:3 
                        s[1] = s[1].Substring(1, s[1].Length - 2);
                        ts += 4; //get opcode

                        ts += 3; //get N1

                        ts += 3; //get N2

                        //this may be contended;
                        int i = Convert.ToInt32(s[1]);
                        if ((i > 16383) && (i < 32768))
                        {
                            ts += contention(ts);

                        }
                        ts += 3; //Write a
                    }

                    break;


                case "adc hl":
                    ts += 15;
                    break;

                case "neg":
                    ts += 8;
                    break;




                default:

                    //listBox2.Items.Add("ACont:"+s[0]);

                    break;



            }


            return ts;
        }
        private void POKE(int address, int value)
        {
            ram[address] = value;
            if (emulationactive && checkVERBOSE.Checked)
            {
                int [] locxy=address_to_coord(address);
                //dashCell(locxy[0], locxy[1], value); //updates screen;
                Graphics G = Graphics.FromImage(buffy);
                setcell(G, locxy[0], locxy[1],1);
                pictureBox1.Image = buffy;
                G.Dispose();


            }

            if (checkStopW.Checked)
            {
                if (address.ToString() == txtDebugAddr.Text)
                {
                    emulationactive = false;
                    listBox2.Items.Add("***STOP: Breakpoint write address " + txtDebugAddr.Text);
                    listBox2.TopIndex = listBox2.Items.Count - 1;
                }
            }

        }

        private int execop(string opcode, int ts)
        {

            if (checkVERBOSE.Checked)
            {
                textBox14.Text = opcode;
                textBox11.Text = ts.ToString();
                listBox2.Items.Add(opcode + " " + ts);
                listBox2.TopIndex = listBox2.Items.Count - 1;
            }

            opcode = opcode.ToLower();
            
            string[] u = opcode.Split(';');
            string[] s = u[0].Split(',');

            s[0] = s[0].Trim();
            if (s[0]!="") if (opcode.Substring(0, 3) == "djn") s[0] = "djnz";

            string[] r = s[0].Split(' ');
            switch (s[0])
            {
                case "jr nz":
                    ts += (reg.a-1) *16;
                    ts += 7;
                    reg.a = 0;
                    moveula(ts);
                    break;

                case "dec a":
                    ts += 4;
                    moveula(ts);
                    break;

                case "ld b":
                    reg.b = Convert.ToInt32(s[1]);
                    ts += 7;
                    moveula(ts);
                    break;
                
                case "djnz":
                    ts += 13 * (reg.b-1);
                    ts += 8;
                    moveula(ts);
                    break;

                case "ld sp":
                    ts += 10;
                    s[1] = s[1].Trim();
                    reg.sp = Convert.ToInt32(s[1]);
                    moveula(ts);

                    break;

                case "ld hl":
                case "ld de":
                case "ld bc":

                    s[1] = s[1].Trim();
                    //always uncontended, 10ts
                    ld(r[1], Convert.ToInt32(s[1]));

                    ts += 10;
                    moveula(ts);
                    break;

                case "push hl":
                case "push de":
                case "push bc":
                    //PUSH RR= PC:4, IR:1, SP:3, SP:3
                    ts += 4; //read pc
                    moveula(ts);
                    ts += 1; //IR
                    moveula(ts);
                    ts += contention(ts); //SP delay
                    moveula(ts);

                    if (r[1] == "hl")
                    {
                        reg.sp--;
                        //ram[reg.sp] = reg.h;
                        POKE(reg.sp, reg.h);

                        ts += 3;
                        moveula(ts);
                        ts += contention(ts); //SP2 delay
                        moveula(ts);
                        ts += 3;
                        reg.sp--;
                        //ram[reg.sp] = reg.l;
                        POKE(reg.sp, reg.l);

                        moveula(ts);
                    }
                    if (r[1] == "de")
                    {
                        reg.sp--;
                        //ram[reg.sp] = reg.d;
                        POKE(reg.sp, reg.d);
                        ts += 3;
                        moveula(ts);
                        ts += contention(ts); //SP2 delay
                        moveula(ts);
                        ts += 3;
                        reg.sp--;
                        //ram[reg.sp] = reg.e;
                        POKE(reg.sp, reg.e);
                        
                        moveula(ts);
                    }
                    if (r[1] == "bc")
                    {
                        reg.sp--;
                        //ram[reg.sp] = reg.b;
                        POKE(reg.sp, reg.b);
                        
                        ts += 3;
                        moveula(ts);
                        ts += contention(ts); //SP2 delay
                        moveula(ts);
                        ts += 3;
                        reg.sp--;
                        //ram[reg.sp] = reg.c;
                        POKE(reg.sp, reg.c);
                        
                        moveula(ts);
                    }
                    //delayed if sp in contended area. (will always be in contended area!)
                    break;

                case "ld (hl)":
                    s[1] = s[1].Trim();
                    ts += 4; //pc:4 read pc
                    moveula(ts);

                    //check if it's a R or a N
                    if (Regex.IsMatch(s[1], @"^[a-zA-Z]+$"))
                    {
                        //LD (HL),r
                        //ram[reg.h * 256 + reg.l] = rd(s[1]);
                        POKE(reg.h * 256 + reg.l, rd(s[1]));
                        
                    }
                    else
                    {
                        ts += 3; //pc:3 read n
                        moveula(ts);
                        // LD(hl),n= PC:4, PC+1=3, hl=3
                        //ram[reg.h * 256 + reg.l] = Convert.ToInt32(s[1]);
                        POKE(reg.h * 256 + reg.l, Convert.ToInt32(s[1]));

                    }
                    ts += contention(ts); //apply contention write hl
                    ts += 3; //hl:3
                    moveula(ts);
                    break;
                case "exx":
                    exx();
                    ts += 4; //pc:4 read pc not contended
                    moveula(ts);
                    break;

                case "nop":

                    ts += 4; //pc:4 read pc not contended
                    moveula(ts);
                    break;

                case "inc hl":
                    //reg.l++;
                    //if (reg.l == 256) { reg.l = 0; reg.h++; reg.h = reg.h > 255 ? 0 : reg.h; }
                    ld("hl", rd("hl") + 1);
                    ts += 6;
                    moveula(ts);
                    break;

                case "add a":
                    s[1] = s[1].Trim();
                    reg.a += Convert.ToInt32(s[1]);
                    reg.a = reg.a > 255 ? reg.a - 256 : reg.a;
                    ts += 7;
                    moveula(ts);
                    break;

                case "ld r":
                    reg.r = reg.a;
                    ts += 8;
                    moveula(ts);
                    break;

                case "ld a":
                    
                    s[1] = s[1].Trim();
                    if (s[1].Substring(0,1) != "(")
                    {
                        //ld a,n
                        reg.a = Convert.ToInt32(s[1]);
                        ts += 7;
                        moveula(ts);
                    }
                    else
                    {
                        //ld a,(NN)                    
                        s[1] = s[1].Substring(1, s[1].Length - 2);
                        ts += 4; //get opcode
                        moveula(ts);
                        ts += 3; //get opcode
                        moveula(ts);
                        ts += 3;
                        moveula(ts);
                        //this may be contended;
                        int i = Convert.ToInt32(s[1]);
                        if ((i > 16383) && (i < 32768))
                        {
                            ts += contention(ts);
                            moveula(ts);
                        }
                        ts += 3;
                        reg.a = i;
                        moveula(ts);
                    }
                    //pc:4,pc+1:3,pc+2:3,nn:3 
                    break;

                case "adc hl":
                    //adc  hl,bc
                    ld("hl", (reg.b * 256 + reg.c) + (reg.h * 256 + reg.l));
                    ts += 15;
                    moveula(ts);
                    break;

                case "neg":
                    reg.a = 256 - reg.a;
                    ts += 8;
                    moveula(ts);
                    break;


                default:
                    if (checkVERBOSE.Checked) listBox2.Items.Add("* Last opcode not executed!");
                    listBox2.TopIndex = listBox2.Items.Count - 1;
                    break;


            }

            //listBox2.Items.Add("out: " + ts);
            return ts;
        }

        private int contention(int ts)
        {
            if ((ts < contst) || (ts > contend))
            {
                return 0;
            }
            int ncycle = (ts - rasterst);
            int raster = ncycle / modelts;
            raster = raster * modelts;
            int noncont = raster + 128;
            int nextcont = raster + modelts;
            int dlygroup = ((ncycle - raster) % 8);

            int delay = cpat[dlygroup]; //8 - dlygroup;
            //delay = delay - 2;
            //delay = delay < 0 ? 0 : delay;

            if ((ncycle >= noncont) && (ncycle < nextcont))
            {
                //raster is in the border
                return 0;
            }
            else if (ncycle >= raster && ncycle < noncont)
            {
                //reading memory
                return delay;
            }
            else
            {
                throw new System.ArgumentException("Out of calc space", "delay");
            }


        }

        private void setcontentionmodel(int index)
        {

            contst = Convert.ToInt32(contmodel[index, 1]); // start of contention
            contend = 57248; //end of contention
            modelts = Convert.ToInt32(contmodel[index, 3]);
            rasterst = contst - modelts; //start of program. it starts 1 raster time before contention
            contpattern = contmodel[index, 2];
            for (int x = 0; x < 8; x++)
            {
                cpat[x] = Convert.ToInt32(contpattern.Substring(x, 1));
            }
            delayloop = contmodel[index, 4];
            resetmodel();
        }



        private void toggleMultiSwap(int x, int y)
        {

                int sm;
                byte sb;
                sm=mlt[x, y];
                sb = bmp[x, y];
                mlt[x, y] = umlt[x, y];
                bmp[x, y] = ubmp[x, y];

            if (!chkNotToggle.Checked)
            {
                umlt[x, y] = sm;
                ubmp[x, y] = sb;
            }
        }

        /// <summary>
        /// x as (0-31), y as (0-23)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private void toggle(int x, int y)
        {
            int l = 8;
            if (radioButton10.Checked) l = 1;


            if (attr[x, y, 9] == 1)
            {
                attr[x, y, 9] = 0;
                //now copy mlt data into cells

                for (int k = 0; k < l; k++)
                {
                    mlt[x, (y * 8) + k] = umlt[x, (y * l) + k];
                    bmp[x, (y * 8) + k] = ubmp[x, (y * l) + k];

                }
            }
            else
            {
                attr[x, y, 9] = 1;
                //now copy spare data into cell
                for (int k = 0; k < l; k++)
                {
                    mlt[x, (y * l) + k] = spare[x, y];
                    bmp[x, (y * l) + k] = sparebmp[x, (y * l) + k];

                }
            }

        }
        private void togglelr(int x, int y, int button)
        {
            int l = 8;
            if (radioButton10.Checked) l = 1;
            if (button == 1)
            {
                attr[x, y, 9] = 0;
                //now copy mlt data into cells

                for (int k = 0; k < l; k++)
                {
                    mlt[x, (y * l) + k] = umlt[x, (y * l) + k];
                    bmp[x, (y * l) + k] = ubmp[x, (y * l) + k];

                }
            }
            else if (button == 2)
            {
                attr[x, y, 9] = 1;
                //now copy spare data into cell
                for (int k = 0; k < l; k++)
                {
                    mlt[x, (y * l) + k] = spare[x, y];
                    bmp[x, (y * l) + k] = sparebmp[x, (y * l) + k];

                }
            }

        }

        private void togglespare(bool spareshown)
        {
            if (spareshown)
            {

            }
        }

        private void checkSPARE_CheckedChanged(object sender, EventArgs e)
        {

        }

        private byte[] packbitmap()
        {
            byte[] f = new byte[6144];
            int c = 0;
            int third, line, row, x;
            for (third = 0; third < 3; third++)
            {
                for (line = 0; line < 8; line++)
                {
                    for (row = 0; row < 8; row++)
                    {
                        int y = (third * 64) + (row * 8) + line;
                        for (x = 0; x < 32; x++)
                        {

                            //byte p = Convert.ToByte((npix[x, y] ? 128 : 0) + (npix[x + 1, y] ? 64 : 0) + (npix[x + 2, y] ? 32 : 0) + (npix[x + 3, y] ? 16 : 0) + (npix[x + 4, y] ? 8 : 0) + (npix[x + 5, y] ? 4 : 0) + (npix[x + 6, y] ? 2 : 0) + (npix[x + 7, y] ? 1 : 0));
                            f[c] = bmp[x, y];

                            c++;
                        }
                    }
                }
            }
            return f;
        }

        private byte[] packattribs()
        {
            int t = 0;
            byte[] s = new byte[768];
            for (int y = 0; y < 192; y += 8)
            {
                for (int x = 0; x < 32; x++)
                {
                    s[t] = (byte)mlt[x, y];
                    t++;
                }
            }
            return s;
        }

        private byte[] packmlt()
        {
            int t = 0;
            byte[] s = new byte[6144];
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    s[t] = (byte)mlt[x, y];
                    t++;
                }
            }
            return s;
        }

        private string getcodeinfo(int x, int y)
        {

            int adres = cycletoaddress(safecycle[cx, cy]);
            int adres2 = adres + 1; //to catch LD SP's
            string tip = adres.ToString() + ":\r\n";
            y = y - (y % 8);
            for (int k = y; k < y + 8; k++)
            {

                string code = processq(k);
                using (StringReader reader = new StringReader(code))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if ((line.Contains(adres.ToString())) || (line.Contains(adres2.ToString())))
                        {
                            tip += k + ":" + line + "\r\n" + reader.ReadLine() + "\r\n";
                        }

                    }

                }
            }
            return tip;
        }

        //Functions -------------------------------------------------------------------------------------------------

        private void resetmachine()
        {
            buffer[0].a = 0;
            buffer[0].b = 0;
            buffer[0].c = 0;
            buffer[0].d = 0;
            buffer[0].e = 0;
            buffer[0].h = 0;
            buffer[0].l = 0;
            buffer[0].i = 254;
            buffer[0].ix = 0;
            buffer[0].iy = 0;
            buffer[0].r = 0;

            ram[32768] = 33;

            reg.a = 0;
            reg.b = 0;
            reg.c = 0;
            reg.d = 0;
            reg.e = 0;
            reg.h = 0;
            reg.l = 0;
            reg.i = 254;
            reg.ix = 0;
            reg.iy = 0;
            reg.r = 0;
            ram[32768] = 33;
        }

        private int calcgroups(int y)
        {
            info[y] = "";
            int total = 0;
            int first = -2;
            int ind = 0;
            bool ig = false;

            int[] uniquedata = new int[256]; //bir satırda yazılması gereken kaç *unique* byte değeri var
            int uniquedatacnt = 0;

            for (int i = 0; i < uniquedata.Length; i++)
            {
                uniquedata[i] = -1;
            }

            for (int x = 0; x < 17; x++)
            {
                groups[y, x, 0] = 0; //grubun taşıdığı toplam tstate (tstate)
                groups[y, x, 1] = 0; //grup blok sayısı (mlt blok(0-31))
                groups[y, x, 2] = 0; //grubun deadline tstate'i (tstate)
                groups[y, x, 3] = 0; //grup kodunun ideal başlangıç zamanı (tstate--sonradan hesaplanır)
                groups[y, x, 4] = 0; //grubun son x koordinatı (0-31)
                groupcode[y, x] = ""; //grubun uygulama kodu
            }



            for (int x = 31; x > -1; x--)
            {
                if (chex[x, y] != 0)
                {

                    if (first < 0) first = x;

                    //add to last group
                    total++; //increase total cells

                    bool udfound = false;
                    for (int u = 0; u < uniquedatacnt; u++)
                        if (uniquedata[u] == mlt[x, y]) udfound = true;

                    if (!udfound)
                    {
                        uniquedata[uniquedatacnt] = mlt[x, y];
                        uniquedatacnt++;
                    }

                    groups[y, ind, 1]++; //add to latest group
                    groups[y, ind, 2] = safecycle[x, y]; // record latest tstate (this group must complete before raster catches)
                    groups[y, ind, 4] = x; //group x location
                    ig = true;
                    if (groups[y, ind, 1] == 12)
                    {
                        //we don't support groups longer than 12 (they will be merged with next group anyway)
                        //so stop this group, and create new one.
                        info[y] += "G" + ind + " Len:" + groups[y, ind, 1] + " (" + groups[y, ind, 2] + " ts) Loc:" + groups[y, ind, 4] + "\r\n";
                        ind++;  //Destroy group if we were in a group (gmax=ind=max group count)
                        ig = false; //not in group
                        first = -1;
                    }

                }
                else
                {
                    if (ig)
                    {
                        info[y] += "G" + ind + " Len:" + groups[y, ind, 1] + " (" + groups[y, ind, 2] + " ts) Loc:" + groups[y, ind, 4] + "\r\n";
                        ind++;  //Destroy group if we were in a group (gmax=ind=max group count)
                        ig = false; //not in group
                        first = 0;
                    }
                    first--; //count seperations
                }
            }
            if (ig)
            {
                //close last open group
                info[y] += "G" + ind + "=" + groups[y, ind, 1] + " (" + groups[y, ind, 2] + " ts) Closing. \r\n";
                ind++;  //Destroy group if we were in a group (gmax=ind=max group count)
            }

            gmax[y] = ind;
            bmax[y] = total;
            umax[y] = uniquedatacnt - 1;
            info[y] += "Blocks:" + total.ToString() + ", Groups:" + ind.ToString();
            info[y] += "\r\n \r\nUnique data count:" + uniquedatacnt.ToString() + " of " + total.ToString() +" total";
            return total;

        }


        private int bufferdataV8(int y)
        {
            if (umax[y] <= 4)
            {
                //good opportunity
                for (int x = 0; x <= umax[y]; x++)
                {

                }



            }
            return 0;
        }

        private int bufferdata(int y)
        {


            //if (spq[0] == 0) return -1;
            int total = 0;
            int ind = 0;
            total = calcgroups(y); //calculate groups funciton
            qptr = 0;

            ind = gmax[y];
            int ptr = 0;
            //reset queue
            for (int x = 0; x < 16; x++)
            {
                spq[x] = 0;
                rq[x] = 0;
            }

            for (int x = 0; x < ind; x++) //process groups one by one
            {
                int g = groups[y, x, 1]; //how many writes needed in this group?


                if ((g > 1))
                {
                    //more than one, needs register pairs

                    spq[ptr] = mltcoordtoaddress(groups[y, x, 4] + g, y); //mark this group for push writing ->basically address to write
                    int z = g - (g % 2); //align 2byte boundary
                    int plen = ptr;
                   
                    for (int k = 2; k <= z; k += 2)
                    {
                        rq[ptr] = coordtodec(groups[y, x, 4] + (g - k), y); //add to reg queue (reversed) -> basically, 2bytes of data to push to above spq address 
                        ptr++; //forward reg pointer 


                    }
                    //pushqueue length (using same index as spq)

                    pql[plen] = ptr - plen;

                    if ((g % 2) == 1)
                    {
                        //Write last value
                        //rq[ptr] = icoordtodec(groups[y, x, 4] - 1, y); //we'll write two bytes anyway!
                        //spq[ptr] = -mltcoordtoaddress(groups[y, x, 4], y); //(negative Value to signify that it's a single byte to write) 
                        //ptr++;
                        addtoq(-mltcoordtoaddress(groups[y, x, 4], y), icoordtodec(groups[y, x, 4] - 1, y));
                    }
                }
                else if (g == 1)
                {
                    //single byte to write

                    addtoq(-mltcoordtoaddress(groups[y, x, 4], y), icoordtodec(groups[y, x, 4] - 1, y));

                }
                else
                {
                    //the group is empty.
                }



            }

            //Sort push groups
            if (ptr == 0) return -1; //no pushes!


            //we got at least one writes
            //we need to reorder spq, so we still have "sorted writes" in 6 registers per group
            int[] bspq = new int[16];
            int[] brq = new int[16];
            int iter = 1;
            bool last = false;
            int stacks = 0;
            ptr--; //fix pointer


            while (true)
            {
                int s = ptr - (iter * 6); //top of stack
                int b = ptr - ((iter - 1) * 6); //bottom of stack
                if (s < 0)
                {
                    s = -1; //trim top
                    last = true; //mark as last iter
                }
                int arrptr = s + 1;
                for (int x = b; x > s; x--)
                {
                    if (spq[x] > 0)
                    {
                        
                        Array.Copy(rq, x, brq, arrptr, pql[x] - stacks);
                        Array.Copy(spq, x, bspq, arrptr, pql[x] - stacks);
                        arrptr += pql[x] - stacks;
                        
                        stacks = 0;

                    }
                }
                if (arrptr <= b)
                {
                    //still have pushes in queue (but we don't now where they belong)
                    Array.Copy(rq, s + 1, brq, arrptr, (b - arrptr) + 1);
                    Array.Copy(spq, s + 1, bspq, arrptr, (b - arrptr) + 1);

                    stacks = (b - arrptr) + 1;
                }
                iter++;
                if (last) break;
            }
            Array.Copy(brq, rq, brq.Length);
            Array.Copy(bspq, spq, bspq.Length);

            return ptr;
        }

        private string processq(int y)
        {

            string code = "";
            int blen = bufferdata(y); //returns number of pushes only
            int totalq = blen + qptr;
            reg = buffer[y];

            #region FirstRasterException
            if (y % 8 == 0) //sync loop
            {
                code += "; +++ --- --- Raster " + y + " (Free) \r\n";
                if (y != 0)
                {
                    if (fazla[y] > 3)
                    {
                        
                        //oops this won't work at all :D
                        //in order to make it sync, we need to get close as 3 ts!
                        string tempstring= fillraster(fazla[y], 0,y);
                        int t2 = Convert.ToInt32(tempstring.Substring(tempstring.Length - 1, 1));
                        fazla[y] = t2;
                        code += tempstring + "\r\n ; Aligned to rasterstart \r\n";
                    }

                    //we don't need to write this line, just repair where we are
                    code += "ld a,(22529) ;Contention fix f" + fazla[y] + "\r\n";
                    if (y == 112)
                    {
                        int debug = 0;
                    }
                    int tsnow = (y * modelts) + rasterst + fazla[y];
                    int result = applycontention("ld a,(22529) ;Contention fix\r\n", tsnow);
                    //fix contention by reading from contended memory.
                    int deadlin = ((y + 1) * modelts) + rasterst; //start of raster!
                    int tisi = deadlin-result; //modelts - (result - ((y * modelts) + rasterst + fazla[y]));
                    code += ";filling for " + tisi + " at " + result+"\r\n";
                    code += fillraster(tisi, result, y) + "\r\n";
                }
                else
                {
                    //special occasion for raster 0, as it's write code is always uncontended.
                    code += fillraster(modelts, rasterst,y) + "\r\n";
                }
                fazla[y + 1] = 0;
                buffer[y + 1] = reg;
                return code;
            } //end of sync loop
            #endregion


            #region GeneratePushes


            string[] regs = { "de", "bc", "hl", "de", "bc", "hl" };

            string tempcode = "";
            int psr = 0;

            //qptr = 0; //reset single byte queue pointer (for addq funciton)
            int lastsp = 0;

            //load until registers are full
            int x = blen; //max queue length (count of two byte pairs)

            int lk = x;   //backup max len.
            while (x >= 0) //loop until queue is empty
            {

                while (x >= 0) //loop until queue is empty *inner
                {

                    if (spq[x] < 0) //if this is a single write
                    {
                        //Single write queue
                        addtoq(spq[x], rq[x]);
                        //deal later
                    }
                    else //it's push queue
                    {
                        if (psr == 3) //check if we used up all 3 regs, swap with spare ones
                        {
                            code += "exx\r\n";
                            //exx();
                        }

                        tempcode = "ld " + regs[psr] + "," + rq[x] + ";" + psr + " #" + dectohex4(rq[x]) + regs[psr] + "xLD"; //now load data into empty register
                        code += tempcode + "\r\n"; //add to main code.

                        //ld(regs[psr], rq[x]); //fill cpu registers for advanced optimizing --not used yet

                        psr++; //forward reg pointer
                        if (psr == 6)
                        {
                            //regs full!

                            x--; //forward queue (remove 1 item from queue)
                            code += ";Regs full. \r\n";
                            break;

                        }

                    }
                    x--; //remove this item
                } //while end           



                string blcd = ""; //init block code
                string subcd = ""; //init sub block code
                for (int k = x + 1; k <= lk; k++) //search for a Stack Point, starting from last written queue item, to length of queue, until we find another write directive 
                {

                    if (spq[k] > 0) //find stack point
                    {
                        //set stack pointer

                        blcd = "ld sp," + spq[k] + "; SORT" + (((spq[k] - 1) % 32)).ToString() + "  \r\n";
                        ld("sp", spq[k]);
                        lastsp = spq[k];
                        for (int j = k; j <= lk; j++) //start pushing enough values until another stack point is reached
                        {

                            psr--;


                            blcd += "push " + regs[psr] + "\r\n";
                            if (psr == 3) { blcd += "exx ;psr2;" + psr + "\r\n"; exx(); }
                            if (regs[psr] == "hl") blcd += "; hlpx\r\n";
                            if (spq[j + 1] != 0) break;
                        }
                        subcd += blcd;
                    }

                }

                /*if (psr > 5) // we got a long push series. we should start pushing values now.
                {
                    //but where?!?! This is an exception. Happens only on queue's
                    //we should get back to queue, search for where they belong, push some of them, then update queue
                    blcd = "";
                    for (int j = psr-1; j >= 0; j--) //start pushing enough values
                    {
                        if (j == 3) { blcd += "exx ;psr-j\r\n"; exx(); }
                        blcd += "push " + regs[j] + "\r\n";
                        if (regs[j] == "hl") blcd += "; hlpx\r\n";
                    }
                    subcd = blcd + subcd;

                }
                psr = 0;//ok regs are empty now
                 */
                code += subcd;
                lk = x;        //reset seperator (last k)



            } //bigger while
            #endregion

            #region SortSingles

            //NOW we got "code" filled with pushes and loads.
            //we should arrange it to spread them among ld hl's if it's possible.

            int offset = (y * modelts) + rasterst; //find offset
            int border = (y * modelts) + rasterst + 128; //find offset
            int deadline = ((y + 1) * modelts) + rasterst; //start of raster!


            // code is filled with pushes. We should sort them.
            // as LDHL's always sorted, we just need to sort pushes
            invertq(); //invert HL queue
            reg = buffer[y]; // reset regs (we'll need to emulate them again)
            string newcode = ""; //old code will be scrapped too
            bool HLvar = false;
            bool exHLvar = false;
            int lastk = 0;

            using (StringReader reader = new StringReader(code))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("exx"))
                    {
                        if (HLvar)
                        {
                            HLvar = exHLvar;
                            exHLvar = true;

                        }
                        else
                        {
                            HLvar = exHLvar;
                            exHLvar = false;
                        }
                    }
                    if (HLvar)
                    {
                        //dirty hl loop
                        //We need to push HL first so we can check for other HL's
                        newcode += line + "\r\n";
                        execop(line, 0);
                        if (line.Contains("hlpx"))
                        {
                            HLvar = false;
                            //hl is clean again
                        }

                    }
                    else
                    {

                        //HL is free, so we can write to it safely
                        if (line.Contains("SORT") || line.Contains("hlxLD"))
                        {
                            if (line.Contains("hlxLD"))
                            {
                                HLvar = true; //HL is dirty, do nothing get back to dirty hl loop
                                newcode += line + "\r\n";
                                execop(line, 0);
                            }
                            else
                            {
                                //This is a SP, so we need to stop loading and check for hl's

                                int sortx = Convert.ToInt32(line.Substring((line.IndexOf("SORT") + 4), 2));
                                //first insert our SP, thus spending more time
                                newcode += line + "\r\n";
                                execop(line, 0); //no ts info given, we need to modify registers, that's all.
                                //next line will be a push, but we need to delay this.
                                //we need to move this further away if possible
                                //start checking HL's
                                //cpu regbuffer = reg; //buffer regs
                                int k = lastk; //restore last queue pointer

                                string backupcode = ""; //we'll generate the code in this string
                                //start loop until we reach end of queue or we reached a number bigger than sortx
                                while (true)
                                {
                                    if (k >= qptr) break; //queue full
                                    string singlecode = putsingle(k);
                                    int sort = Convert.ToInt32(singlecode.Substring(singlecode.Length - 3));

                                    if ((sort >= sortx))
                                    {
                                        //this is the point we need to insert our push
                                        //first add sorted code
                                        newcode += backupcode + "\r\n";
                                        lastk = k;
                                        break;
                                    }
                                    else
                                    {
                                        //adding code into backupcode
                                        singlecode = singlecode.Substring(0, singlecode.Length - 3);
                                        using (StringReader reader2 = new StringReader(singlecode))
                                        {
                                            string line2;
                                            while ((line2 = reader2.ReadLine()) != null)
                                            {
                                                execop(line2, 0); //modifys registers
                                            }
                                        }

                                        backupcode += singlecode + "\r\n";
                                        k++;

                                        if (k >= qptr)
                                        {
                                            //no more hl's. we can inject our hl's here
                                            newcode += backupcode + "\r\n";
                                            lastk = k;

                                            break;
                                        }

                                    }
                                }

                            }

                        }
                        else
                        {

                            //if (!firstSP)
                            //{
                            //we are just loading some values and spend some time
                            newcode += line + "\r\n";
                            execop(line, 0); //no ts info given, we need to modify registers, that's all.
                            //}

                        }
                    }



                }
            }


            if (lastk < qptr) //we still have some code in the queue!
            {
                for (int k = lastk; k < qptr; k++)
                {
                    string singlecode = putsingle(k);
                    singlecode = singlecode.Substring(0, singlecode.Length - 3);

                    using (StringReader reader2 = new StringReader(singlecode))
                    {
                        string line2;
                        while ((line2 = reader2.ReadLine()) != null)
                        {
                            execop(line2, 0); //modifys registers
                        }
                    }
                    newcode += singlecode + "\r\n";
                }
            }
            code = newcode;
            // end
            #endregion

            //start of timing loop
            int bestfit = modelts;
            int bestka = 0;
            int ka = modelts;
            int found = 0;
            for (int k = 0; k < (modelts + 1); k++) //align to bottom by testing!!! --ugly solution
            {

                ka = k;//ka is padding tstates
                #region test alignment
                int bof = offset + k;

                using (StringReader reader = new StringReader(code))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        bof = applycontention(line, bof);
                    }
                }

                if (deadline - bof == 0)
                {
                    found = 1;
                    bestfit = 0;
                    break; //done!
                }
                else
                {
                    if ((deadline - bof) >= 0)
                    {
                        if ((deadline - bof) < bestfit) //optimize best fit stack
                        {
                            bestfit = deadline - bof;
                            bestka = k;
                            found = 2;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                #endregion

            }
            if (found == 2)
            {
                //shit we couldn't be able to align the code to the bottom.
                //we should use bestfit
                ka = bestka;
                code += "; Warning:not fit\r\n";
                code += "; should overflow: -" + bestfit + "\r\n";
                //listBox2.Items.Add("Not Aligned Line:" + y);
            }
            if (found == 0)
            {
                //This is big! it will never fit!
                code += "; Error: TOO BIG\r\n";
                ka = 0;
                bestfit = 0;
            }


            
            //Finally, store "write part" on found (or not found) location.
            offset += ka; //offset is start of raster. KA is start of write part (0-modelts)
            int writelen = offset; // writelen is start of raster+ka obviously.
            newcode = ";>>> ------ Write start cycle:" + offset + " ------ <<<\r\n";

            if (offset == 21503)
            {
                int debug = 0;
            }
            int leak = 0;
            using (StringReader reader = new StringReader(code))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {

                    int oldoffset = offset;
                    offset = applycontention(line, offset);

                    if ((offset - writelen > modelts) && checkBox7.Checked) //check if we are going over raster!
                    {
                        
                        
                        //no more ts :(
                        offset = oldoffset;
                        //ka = modelts - (offset - writelen);
                        newcode += ";Overrun protected.\r\n";
                        //listBox2.Items.Add("Protected Line:" + y);
                        
                        //BUT! 
                        //this last opcode may leave empty tstates, which we need to fill right now.
                        int emptyts=modelts - (offset - writelen);
                        if (emptyts>0)
                        {
                            //oops we got a problem!
                            //empty tstates!
                            if (emptyts > 3)
                            {
                                //which we can fix now :)
                                string tempstring = fillraster(emptyts, offset,y);
                                int t2 = Convert.ToInt32(tempstring.Substring(tempstring.Length - 1, 1));
                                if (t2 != 0)
                                {
                                    //this should leak into next raster!
                                    leak = t2;
                                }
                                offset += (emptyts-t2);
                            }
                            else
                            {
                                leak = emptyts;
                            }

                        }
                        
                        break; //if so, break from while()
                    }
                    newcode += line;
                    newcode += "    ;(" + (offset - oldoffset) + ")\r\n";


                }
            }
            //now check where are we :D
            int total = (offset - writelen);
            code = newcode;
            code += "; Total:" + total + " Ka:" + ka + " Fazla:" + fazla[y] + "\r\n";
            if (total + ka > modelts)
            {
                //oops. not enough time!
                fazla[y + 1] = -((total + ka) - modelts);
                code += ";ERROR RASTER OVERRRUN!\r\n";
                //listBox2.Items.Add("Overrun Line:" + y);

            }
            else
            {
                //code is ok, fitted in one single raster
                //now we need to fill what's left empty
                int t = ka + fazla[y]; //tstates to fill is k+overflow from last raster.
                
                fts[y] = t;
                //if we are at the y=1 then put sync op

                int tisi = 0;
                string fixtr = "";
                if (y == 1)
                {
                    fixtr = "ld a,(22528) ;Contention fix\r\n";
                    int result = applycontention("ld a,(22528) ;test", (y * modelts) + rasterst - fazla[y]);
                    //fix contention by reading from contended memory.
                    tisi = (result - ((y * modelts) + rasterst - fazla[y])); //ld a, kaç ts tuttu?

                }


                string tempstring = fillraster((t - tisi), ((y * modelts) + rasterst - fazla[y]) + tisi,y); // FILL TSTATES with IDLE
                code = fixtr + tempstring + "\r\n" +code;

                int t2 = Convert.ToInt32(tempstring.Substring(tempstring.Length - 1, 1));

                if (t2 != 0)
                {
                    //couldn't made it!
                    #region repoll
                    //re-evaluate
                    // this is getting spagetti code. More explaining:
                    // yukarıda yaptığımız şey, şöyle, önce ld ile yükleme yapıyoruz, sonra push ile çiftleri yazıyoruz, sonra (HL) ile tekleri yazıyoruz
                    // ardından bu yazım grubunu raster'ın sonuna dayıyoruz. Bu herzaman başarılı olamayabiliyor. Bu durumda en yakın değeri buluyoruz.
                    // Rasterın sonu doldurulunca baş kısmındaki boşluğu doldurmak gerekiyor. 
                    // bunu da dolduruyoruz. Ama nedense bu da herzaman dolmuyor. Bu daha büyük bir problem çünkü alt kısımın yerine oturduğu varsayılarak
                    // ts hesabı tutmuş oluyoruz. Bu durumda tüm hesaplar boşa gitmiş oluyor. Bütün kodu baştan zamanlama hesabına 
                    // tabi tutmak gerekiyor. İşte bu rutin, o rutin. Al baştan.
                    offset = (y * modelts) + rasterst - fazla[y]; //reset start ts
                    writelen = offset; //backup startts
                    newcode = ";re-polling ts:" + offset + "\r\n"; //dump info
                    listBox2.Items.Add("*** Repolled Line:" + y);
                    listBox2.TopIndex = listBox2.Items.Count - 1;
                    using (StringReader reader = new StringReader(code))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {

                            int oldoffset = offset;
                            offset = applycontention(line, offset);

                            if (((offset - writelen) > (modelts + fazla[y])) && checkBox7.Checked) //check if we are going over protection!
                            {

                                offset = oldoffset;
                                newcode += ";Overrun protection fired!\r\n";
                                break; //if so, break from while()
                            }
                            newcode += line;
                            newcode += "    ;(" + (offset - oldoffset) + ")\r\n";


                        }
                    }
                    t = (((y + 1) * modelts) + rasterst) - offset;
                    code = newcode + "\r\n;New Total:" + (offset - writelen) + "\r\n"; ;
                    #endregion

                }
                else if (found == 2)
                {
                    t = bestfit;
                }
                else
                {
                    t = 0;
                }
                code += ";excess ts=" + t + "\r\n"; ;
                fazla[y + 1] = t + leak;

                //return "; Raster " + y + ":\r\n" + code;

                //END OF WAIT

            }

            //buffer[y + 1] = emulateraster(code,buffer[y];
            buffer[y + 1] = reg;
            codes[y] = "; +++ --- --- Raster " + y + ": --- --- +++ C:"+reg.c+"\r\n" + code;
            return codes[y];
        }  // <--- HELLO, CODE GENERATION IS HERE!!!

        public string putsingle(int k)
        {

            string blc = "";
            int sort = 99;
            //for (int k = 0; k < qptr; k++)

            cpu backreg = reg;
            if (rd("hl") != -q[k, 0])
            {
                blc += "ld hl," + -q[k, 0] + " ; SORT" + ((-q[k, 0] - 22528) % 32) + "  \r\n";
                ld("hl", -q[k, 0]);
                sort = ((-q[k, 0] - 22528) % 32);
            }

            bool match = false;
            for (int j = 0; j < 6; j++)  //check registers!
            {
                if (rd(rlist[j]) == (q[k, 1] % 256))
                {
                    blc += "ld (hl)," + rlist[j] + " ;" + rd(rlist[j]) + "\r\n";

                    match = true;
                    break;
                }
            }

            if (!match) blc += "ld (hl)," + (q[k, 1] % 256) + "\r\n";

            //damn singles are easy
            reg = backreg;
            return blc + "  0" + sort;
        }

        private string generatecode()
        {

            int topraster = Convert.ToInt32(textTOP.Text) * 8; //starting raster
            int bottomraster = 7 + Convert.ToInt32(textBOTTOM.Text) * 8; //end raster

            int toprasterts = rasterst + (modelts * topraster); //this is needed when emulating the code
            int toprasterdelay = (modelts * topraster);
            textBox12.Text = toprasterts.ToString();           //update emulation start cycle

            int firstaddress = coordtoaddress(0, (topraster / 8)); //Attrib addres to refresh
            int refreshlegth = coordtoaddress(31, (bottomraster / 8)) - firstaddress; //refresh length
            refreshlegth++;
            string code = "; Generating for " + comboBox2.Items[comboBox2.SelectedIndex] + "\r\n";

            code += "org 32768\r\nhalt\r\ndi\r\n";
            if (checkKEY.Checked)
            {
                //backup basic
                code += "ld (stackpointer),sp\r\nld sp,stackpointer\r\nPush HL\r\nPush BC\r\nPush DE\r\nPush AF\r\nexx\r\nPush HL\r\nPush BC\r\nPush DE\r\nex af,af'\r\nPush AF\r\nPUSH IX\r\nPUSH IY\r\nld (stackpointer+2),sp\r\n";

            }


            /*
            //copy/depack bitmap
            if (radioMlz.Checked)
            {
                code += ";MegaLZ Depacking\r\n" + textMegaLZ.Text + "\r\n";
            }
            else if (radioZx7.Checked)
            {
                code += ";ZX7 Depacking\r\n" + textZX7.Text + "\r\n";
            }
            else if (radiozx0.Checked)
            {
                code += ";ZX0 Depacking\r\n" + txtZX0.Text + "\r\n";
            }
            else
            {
                code += "LD HL,bitmap\r\nLD DE,16384\r\nLD BC,6144\r\nLDIR\r\n";
            }*/

            switch (comboBox5.SelectedIndex)
            {
                case 1: // zx0
                    code += ";ZX0 Depacking\r\n" + txtZX0.Text + "\r\n";
                    break;
                case 2: // zx7
                    code += ";ZX7 Depacking\r\n" + textZX7.Text + "\r\n";
                    break;
                case 3: // mlz
                    code += ";MegaLZ Depacking\r\n" + textMegaLZ.Text + "\r\n";
                    break;
                default: // none
                    code += "LD HL,bitmap\r\nLD DE,16384\r\nLD BC,6144\r\nLDIR\r\n";
                    break;
            }


            //setup interrupt
            code += "\r\ngoraster:\r\n;setup interrupt\r\n \r\nld hl,65021\r\nld (hl),201\r\nld   hl,65024\r\nld   de,65025\r\nld   (hl),253\r\nld   b,e\r\nld   c,e\r\nldir\r\nld   a,254\r\nld   i,a\r\nim   2\r\n";

            if (comboBox3.SelectedIndex > 0)
            {
                //setup border
                code += ";setup border\r\nld a," + (comboBox3.SelectedIndex - 1) + "\r\nout (254),a\r\n";
            }

            if (chkUseBank7.Checked)
            {
                code += ";setup secondary buffer\r\nld bc,0x7FFD\r\nld a, 7\r\nout (c), a"; //page bank 7 (128k's secondary swap buffer)  
            }


            //Reload default attributes
            code += ";main loop\r\nbong:\r\n";

            //Refresh as much as we can, that means we can refresh
            int refreshlength2 = refreshlegth > 660 ? refreshlegth - 660 : 0;
            if (refreshlength2 != 0) //This part MAY be contended. We have to avoid writing attribs here if possible
            {
                code += "ld hl,attributes\r\nld de," + firstaddress + "\r\nld bc," + refreshlength2 + "\r\nldir\r\n";
            }

            //keyboard code
            if (checkKEY.Checked)
            {
                code += "ld c,#FE\r\nxor a\r\nin a,(C)\r\ncpl\r\nand #1f\r\njr nz,exitloop\r\njr start\r\nexitloop:\r\njp restorebasic\r\n";
            }

            //HALT -- Wait for new cycle range
            code += "start:\r\nei\r\nhalt\r\ndi\r\n";
            //Now we are at ts34~
            int tsnow = 34;

            //reload excess attributes if needed
            int refreshlength3 = refreshlegth - refreshlength2;
            if (refreshlength2 == 0) //if we need to reload colours again
            {
                code += "ld hl,attributes\r\nld de," + firstaddress + "\r\n";
                tsnow += 20;
            }

            if (refreshlength3 != 0) //Do we still have to refresh colors?
            {
                code += "LD BC," + refreshlength3 + "\r\nLDIR\r\n ";
                tsnow += 10; //ld bc
                tsnow += (refreshlength3 * 21);
                tsnow -= 4; //last opcode took 17 only
            }
            //code += "LD BC," + tsnow + "\r\n";
            //tsnow += 10;

            int needed = 0;
            if ((refreshlength3 < 660) && (refreshlength3 >= 0))
            {
                int waitloop = ((((rasterst - 68) + toprasterdelay) - 24) - tsnow) / 26; //24 comes from following asm code:
                waitloop--;
                if (waitloop > 1)
                {
                    //wait loop (depending on the raster)
                    code += "\r\nld bc," + waitloop + "\r\n";
                    code += "loop:\r\ndec bc\r\nld a,b\r\nor c\r\njr nz, loop\r\n";
                    tsnow += (waitloop * 26) + 20; //last opcode took 17 only hence 20 instead of 24
                }
                needed = 22 + (((((rasterst - 68) + toprasterdelay) - 24) - tsnow) % 26);
            }
            //Burayı tamir et;

            code += ";Ts Now:" + tsnow + "\r\n";

            code += fillraster(((rasterst - tsnow) - 68), tsnow,999) + "\r\n";
            //at rasterst-68
            //now initialization (we are now, rasterst-68
            code += "ld hl,0\r\nld bc,0\r\nld de,0\r\nexx\r\nld hl,0\r\nld bc,0\r\nld de,0\r\nxor a\r\n"; //this is 68ts
            //add model specific delay

            listBox2.Items.Clear();

            resetmachine();
            code += "; Raster Start\r\n";
            //now we have to be at RasterSt
            for (int y = topraster; y <= bottomraster; y++)
            {

                code += processq(y);// linecode(y);
            }

            code += "jp bong\r\n";

            code += generatedefb();

            if (checkKEY.Checked)
            {
                code += "restorebasic:\r\ndi\r\nLD sp,(stackpointer+2)\r\nPOP IY\r\nPOP IX\r\nPOP AF\r\nex af,af'\r\nPop DE\r\nPop BC\r\nPop HL\r\n\r\nexx\r\npop af\r\npop de\r\npop bc \r\npop hl\r\nld sp,(stackpointer)\r\nim 1\r\nei\r\nret\r\n stack:\r\n defb 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0\r\n stackpointer:\r\n defb 0,0,0,0";

            }

            code += "\r\nend 32768";

            return code;

        }

        private string generatedefb()
        {
            string defb = "attributes:\r\n";
            for (int y = 0; y < 192; y += 8)
            {
                defb += "defb ";
                for (int x = 0; x < 32; x++)
                {
                    defb += "#" + dectohex(mlt[x, y]) + ",";
                }
                defb = defb.TrimEnd(',');
                defb += "\r\n";
            }

            defb += "\r\nbitmap:\r\n";


            
            /*obsolete code 
            if (radioMlz.Checked || radioZx7.Checked || radiozx0.Checked)
            {
                string filenom="\\packed.bin";
                if (radioMlz.Checked) { runLZ(); }
                if (radioZx7.Checked) { runzx7();  }
                if (radiozx0.Checked) { runZX0();  }
              */
  
            // Now pack data
            if (comboBox5.SelectedIndex == 1 || comboBox5.SelectedIndex == 2 || comboBox5.SelectedIndex == 3)
            {
                string filenom = "\\packed.bin";
                if (comboBox5.SelectedIndex == 3) { runLZ(); }
                if (comboBox5.SelectedIndex == 2) { runzx7(); }
                if (comboBox5.SelectedIndex == 1) { runZX0(); }
                

                BinaryReader b = new BinaryReader(File.Open(Application.StartupPath + filenom, FileMode.Open));
                // 2.
                // Position and length variables.

                // Use BaseStream.
                int length = (int)b.BaseStream.Length;
                byte[] array = new byte[length];
                int n = b.BaseStream.Read(array, 0, length);



                int cnt = 0;
                defb += ";Len:" + array.Length + "\r\n";
                // Loop through contents of the array.
                foreach (byte element in array)
                {
                    if (cnt == 0)
                    {
                        defb = defb.TrimEnd(',');
                        defb += "\r\n";
                        defb += "defb ";
                    }

                    defb += "#" + dectohex(element) + ",";

                    cnt++;
                    cnt = cnt > 32 ? 0 : cnt;
                }
                defb = defb.TrimEnd(',');
                defb += "\r\n";
            }
            else
            {
                for (int u = 0; u < 3; u++)
                {
                    for (int t = 0; t < 8; t++)
                    {
                        for (int y = 0; y < 8; y++)
                        {
                            defb += "defb ";
                            for (int x = 0; x < 32; x++)
                            {
                                defb += "#" + dectohex(bmp[x, (u * 64) + (t + (y * 8))]) + ",";
                            }
                            defb = defb.TrimEnd(',');
                            defb += "\r\n";
                        }
                    }
                }
            }
            return defb;
        }

        /// <summary>
        /// Freets: TS to fill
        /// ts: start ts, as BC shows where we are on assembly
        /// </summary>
        /// <param name="freets"></param>
        /// <param name="ts"></param>
        /// <returns></returns>
        private string fillraster(int freets, int ts, int rasterno) //always fits perfectly if freets>24
        {
            int t = freets;
            int it = t;
            int lts;
            int loopcounter = 0;

            string code = ";::: * "+rasterno+" * ::: Need to fill " + freets + " @ts:"+ts+"\r\n";
            /*
            if (t > 34)
            {
                lts = ts + (it - t);
                int loop = (t+5) / 23; //4 is added because last jr nz takes 7 instead of 12
                string tempcode = "label" + lts + "      ld a, "+loop+"; dec a; jr nz, label" + lts +  "\r\n";

            }
             */


            if (chkUseLoop.Checked && (t > 33))
            {
                #region disabled using b register
                /*
                //find b
                int b = t - 15; // ld b,n (7ts) + last djnz wait_loop (8ts) = 15ts
                b = ((int)(b / 13))+1; // djnz (13) +1 add missing loop
                int remaining=t-(13*(b-1)+15);

                if ((remaining==1) || (remaining==2) || (remaining==3) || (remaining==5)) //if filled ts leaves only 2 or 3 ts, we can't ever fill it to zero in next precise fill part.
                {
                    b--; //this leaves 14 to 18ts to fill
                }

                string tempcode = "ld b," + b + "\r\n";
                code += tempcode;
                t = t - 7;
                execop(tempcode, 0);

                //tempcode = "waitloop" + rasterno.ToString().PadLeft(3, '0') + loopcounter.ToString().PadLeft(3, '0') + ":\r\n";
                lts = ts + (it - t);
                tempcode = "waitloop" + lts + ":\r\n";
                code += tempcode;
                
                tempcode = "djnz waitloop" +lts+ "\r\n";
                code += tempcode; 
                t = t - (((b-1) * 13) + 8);
                execop(tempcode, 0);
                */
                #endregion


                int a = t - (18); //if need to fill=18 than this is ok.

                a = ((int)(a / 16))+1; //can't be 0

                int remaining = t - (16 * (a - 1) + 18);

                if ((remaining == 1) || (remaining == 2) || (remaining == 3) || (remaining == 5)) //if filled ts leaves only 2 or 3 ts, we can't ever fill it to zero in next precise fill part.
                {
                   if (a>2) a--; //this leaves 17 to 22 more ts to fill
                }

                string tempcode = "ld a," + a + "\r\n";
                code += tempcode;
                t = t - 7;
                execop(tempcode, 0);

                lts = ts + (it - t);
                tempcode = "waitloop" + lts + ":\r\n";
                code += tempcode;

                tempcode = "dec a\r\n";
                code += tempcode;
                t = t - 4; //spend 4 ts
                execop(tempcode, 0);

                tempcode = "jr nz, waitloop" + lts + "\r\n";
                code += tempcode;
              
                t = t - (a - 1) * 16; //12 ts for "jr", 4 for "dec a" =16ts
                t = t - 7; //last jr = 7 instead of 11 because we spend 4 ts at previous dec a

                execop(tempcode, 0);

            }
            


                while (t > 24)
                {

                    lts = ts + (it - t);
                    string tempcode = "ld sp," + lts + "\r\n";
                    //string tempcode = "ld sp,12593\r\n"; //to make it megaLZ compatible 
                    code += tempcode;
                    execop(tempcode, 0);
                    t = t - 10;
                }
            
            //we need to optimize last part 14-23
            switch (t)
            {
                case 4:
                case 5:
                    code += "nop \r\n";
                    t = t - 4;
                    //nop
                    break;
                case 6:
                    code += "inc hl\r\n";
                    t = t - 6;
                    ld("hl", rd("hl") + 1);
                    break;
                case 7:
                    code += "add a,0\r\n";
                    t = t - 7;

                    break;
                case 8:
                    code += "neg\r\n";
                    t = t - 8;
                    execop("neg", 0);
                    break;
                case 9:
                    code += "ld r,a\r\n";
                    reg.r = reg.a;
                    t = t - 9;
                    break;
                case 10:
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);
                    break;
                case 11:
                    code += "nop \r\n";
                    t = t - 4;
                    code += "add a,0\r\n";
                    t = t - 7;
                    break;
                case 12:
                    code += "neg\r\nnop \r\n";
                    t = t - 12;
                    execop("neg", 0);
                    break;
                case 13:
                    code += "ld a,(32768)\r\n";
                    t = t - 13;
                    reg.a = ram[32768];
                    break;
                case 14:
                    code += "add a,0\r\n";
                    t = t - 7;
                    code += "add a,0\r\n";
                    t = t - 7;
                    break;
                case 15:
                    code += "neg\r\nadd a,0\r\n";
                    execop("neg", 0);
                    t = t - 15;
                    break;
                case 16:
                    code += "neg\r\nneg\r\n"; //double neg=no effect
                    t = t - 16;
                    break;
                case 17:
                    code += "add a,0\r\n";
                    t = t - 7;
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);
                    break;
                case 18:
                    code += "neg\r\n";
                    t = t - 8;
                    execop("neg", 0);
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    break;
                case 19:
                    code += "neg\r\nadd a,0\r\n";
                    t = t - 15;
                    execop("neg", 0);
                    code += "nop \r\n";
                    t = t - 4;
                    break;
                case 20:
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);
                    break;
                case 21:
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    code += "nop \r\n";
                    t = t - 4;
                    code += "add a,0\r\n";
                    t = t - 7;
                    break;
                case 22:
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    code += "neg\r\nnop \r\n";
                    t = t - 12;
                    execop("neg", 0);

                    break;
                case 23:
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    code += "ld a,(32768)\r\n";
                    t = t - 13;
                    reg.a = ram[32768];
                    break;
                case 24:
                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    lts = ts + (it - t);
                    code += "ld sp," + lts + "\r\n";
                    t = t - 10;
                    execop("ld sp," + lts, 0);

                    code += "nop \r\n";
                    t = t - 4;
                    break;


            }
            code += "; Filled " + (it - t) + ", left:" + t;
            return code;
        }

        private string[] opcodes(string opcode) { int address = 0; int data = 0; return opcodes(opcode, address, data); }
        private string[] opcodes(string opcode, int address) { int data = 0; return opcodes(opcode, address, data); }
        private string[] opcodes(string opcode, int address, int data)
        {
            string[] o = new string[3];
            int t = 0;

            switch (opcode)
            {


                case "sp":
                    o[1] = "ld sp," + address.ToString();
                    o[2] = "Set the stack pointer";
                    t = 10;

                    break;

                case "hl":
                    o[1] = "ld hl," + address.ToString();
                    o[2] = "Load 2 byte to hl";
                    t = 10;

                    break;
                case "de":
                    o[1] = "ld de," + address.ToString();
                    o[2] = "Load 2 byte to de";
                    t = 10;

                    break;
                case "bc":
                    o[1] = "ld bc," + address.ToString();
                    o[2] = "Load 2 byte to bc";
                    t = 10;

                    break;
                case "ph":
                    o[1] = "push hl";
                    o[2] = "push hl into stack";
                    t = 11;

                    break;

                case "pd":
                    o[1] = "push de";
                    o[2] = "push de into stack";
                    t = 11;

                    break;

                case "pb":
                    o[1] = "push bc";
                    o[2] = "push bc into stack";
                    t = 11;

                    break;

                case "h1":
                    o[1] = "ld (hl)," + data.ToString();
                    o[2] = "write 1 byte to address at hl.";
                    t = 10;

                    break;


            }
            o[3] = t.ToString();
            return o;
        }

        private void loader(string filename) { loader(filename, 0); }
        private void loader(string filename, int mode)
        {
            bool isEXTENDEDSCR = false;
            using (BinaryReader b = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                // 2.
                // Position and length variables.
                int pos = 0;
                int y = 0;
                int x = 0;
                int loadmode = 0; //0=mlt, 1=scr, 2=bin
                // 2A.
                // Use BaseStream.
                int length = (int)b.BaseStream.Length;
                switch (length)
                {
                    
                    case 12352:
                        mode = 10; //*not loadmode
                        isEXTENDEDSCR = true;
                        break;

                    case 12288:
                        if (Path.GetExtension(filename).Equals(".scr", StringComparison.OrdinalIgnoreCase)) 
                        {
                            mode = 10; //it looks like mlt but an extended SCR
                            isEXTENDEDSCR = true;
                        }
                        loadmode = 0;
                        break;

                    case 6912:
                        loadmode = 1;
                        break;

                    case 6144:
                        loadmode = 2;
                        break;
                }
                if (mode > 9) loadmode = mode - 10;



                int n = b.BaseStream.Read(tape, 0, 6144);

                //Load binary portion
                #region diz

                for (int l = 0; l <= 2; l++)
                {
                    for (int j = 0; j <= 7; j++)
                    {
                        for (int u = (2048 * l) + (j * 32); u <= (2048 * (l + 1)) - 1; u += 256)
                        {
                            for (int z = 0; z <= 31; z++)
                            {

                                if (loadmode != 3)
                                {
                                    bmp[x, y] = tape[u + z];
                                }
                                else
                                {
                                    sparebmp[x, y] = tape[u + z];
                                }

                                x++;
                                if (x > 31) { x = 0; y++; }

                            }
                        }
                    }
                }
                //s = scrn;
                if (loadmode == 2)
                {
                    //bitmap loading done
                    return;
                }

                #endregion

                //load attribs
                y = 0;
                x = 0;
                b.BaseStream.Position = 6144;
                pos = 6144;

                if (mode == 10)
                {
                    byte[] tapeC = new byte[6144];
                    int nc = b.BaseStream.Read(tapeC, 0, 6144);
                    //EXTENDED SCR
                    for (int l = 0; l <= 2; l++)
                    {
                        for (int j = 0; j <= 7; j++)
                        {
                            for (int u = (2048 * l) + (j * 32); u <= (2048 * (l + 1)) - 1; u += 256)
                            {
                                for (int z = 0; z <= 31; z++)
                                {

                                    if (loadmode != 3)
                                    {
                                        mlt[x, y] = tape[u + z];
                                    }
                                    else
                                    {
                                        spare[x, y] = tape[u + z];
                                    }

                                    x++;
                                    if (x > 31) { x = 0; y++; }

                                }
                            }
                        }
                    }
                }
                else
                {
                    //REGULAR MLT
                    while (pos < length)  //loading attributes (at the end of the file)
                    {
                        // 3.
                        // Read integer.
                        byte v = b.ReadByte();

                        if (loadmode != 3)
                        {
                            mlt[x, y] = v;

                        }
                        else
                        {
                            spare[x, y] = v;
                        }
                        if (loadmode == 1)
                        {
                            //scr load
                            for (int k = 1; k < 8; k++)
                            {
                                mlt[x, y + k] = v;
                            }
                        }
                        chex[x, y] = 1;
                        x++;
                        if (x > 31)
                        {
                            x = 0;
                            if ((loadmode == 0) || (loadmode == 3))
                            {
                                //mlt loading
                                y++;
                            }
                            else
                            {
                                //scr loading
                                y += 8;
                            }
                        }
                        if (y > 191) break;

                        // 4.
                        // Advance our position variable.
                        pos += sizeof(byte);
                    }
                }

            }


        }


        private void runLZ()
        {
            if (File.Exists("packed.bin"))
            {
                File.Delete("packed.bin");
            }
            string filename = "lazarus.bits";
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
            byte[] f = packbitmap();
            export(filename, f);

            // Start the child process.
            System.Diagnostics.Process p = new System.Diagnostics.Process();

            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = Application.StartupPath + "\\megalz.exe";
            p.StartInfo.Arguments = "lazarus.bits packed.bin";
            //p.StartInfo.UseShellExecute = true;
            p.StartInfo.CreateNoWindow = true;

            p.Start();

            p.WaitForExit();


        }

        private void runZX0()
        {
            if (File.Exists("packed.bin"))
            {
                File.Delete("packed.bin");
            }

            string filename = "lazarus.bits";
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
            byte[] f = packbitmap();
            export(filename, f);

            // Start the child process.
            System.Diagnostics.Process p = new System.Diagnostics.Process();

            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = Application.StartupPath + "\\zx0.exe";
            p.StartInfo.Arguments = "-f lazarus.bits packed.bin";
            //p.StartInfo.UseShellExecute = true;
            p.StartInfo.CreateNoWindow = true;

            p.Start();

            p.WaitForExit();


        }

        private void runzx7()
        {
            if (File.Exists("packed.bin"))
            {
                File.Delete("packed.bin");
            }
            string filename = "lazarus.bits";
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
            byte[] f = packbitmap();
            export(filename, f);

            // Start the child process.
            System.Diagnostics.Process p = new System.Diagnostics.Process();

            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = Application.StartupPath + "\\zx7.exe";
            p.StartInfo.Arguments = "lazarus.bits packed.bin";
            //p.StartInfo.UseShellExecute = true;
            p.StartInfo.CreateNoWindow = true;

            p.Start();

            p.WaitForExit();


        }

        private void export(string filename, byte[] f)
        {
            using (BinaryWriter b = new BinaryWriter(File.Open(filename, FileMode.Create)))
            {
                // 3. Use foreach and write all 12 integers.
                foreach (byte i in f)
                {
                    b.Write(i);
                }
            }
        }

        private void detectstandards()
        {
            int co = 0;
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    checkstandards(x, y * 8);




                }
            }

            /*for (int y = 0; y < 192; y++)
            {
                processq(y);
            }*/
            //textBox5.Text = co.ToString();
        }

        private bool checkstandards(int x, int y)
        {
            y = y / 8;
            bool same = true;
            for (int k = 1; k < 8; k++)
            {
                if (mlt[x, ((y * 8) + k) - 1] != mlt[x, ((y * 8) + k)])
                {
                    same = false;
                    break;
                }
            }
            clash[x, y] = same;

            if (same)
            {
                //this block needs no multicolor time

                attr[x, y, 8] = getpaper(mlt[x, (y * 8)]);

                for (int k = 0; k < 8; k++)
                {
                    chex[x, (y * 8) + k] = 0;
                }

            }
            else
            {

                //mark proper multicolour marks

                for (int k = 7; k > 0; k--)
                {
                    if (mlt[x, ((y * 8) + k) - 1] != mlt[x, ((y * 8) + k)])
                    {

                        chex[x, (y * 8) + k] = 1;
                    }
                    else
                    {
                        chex[x, (y * 8) + k] = 0;
                    }
                }

                if (mlt[x, ((y * 8) + 1)] == mlt[x, (y * 8)])
                {

                    chex[x, (y * 8)] = 0;
                }
            }
            return same;
        }

        private void fixattr()
        {

            listBox1.Items.Clear();

            int[] r = new int[3];
            byte[] used = new byte[128];
            int maxk = 0;
            used[0] = (byte)mlt[0, 0]; //"used" is the table of colours. To make optimizations, we are trying to  minimise variations



            //now, first GENERATE TABLE OF COLOURS
            //           -------------------------

            for (int y = 0; y < 24; y++)
            {
                //scan clash based Y
                for (int x = 0; x < 32; x++) //scan every horizontal cell
                {


                    for (int z = 0; z < 8; z++) //scan every raster top to bottom
                    {
                        bool check = false; //reset color matching switch
                        r = getattr((byte)mlt[x, (y * 8) + z]); //load colours to r[0]=ink, [1]=paper (both includes brightness)

                        for (int k = 0; k <= maxk; k++) //now check this color with other colors already on the table
                        {
                            int[] j = getattr(used[k]); //load a colour from "used" Table
                            if (r[0] == j[0])           //check if we got a perfect match by ink AND paper colour
                            {
                                if (r[1] == j[1])
                                {
                                    //it's a match! but do nothing
                                    check = true; //denotes if we got a match
                                    break; //as we got a match, we stop checking, this colour is on the table
                                }
                            }
                            else if ((r[1] == j[0]) && (r[0] == j[1])) //perfect match not found, check an inverted match
                            {
                                //it's a match! but inverted

                                mlt[x, (y * 8) + z] = putattr(r[1], r[0]); //invert colors

                                bmp[x, (y * 8) + z] ^= 0xff;  //invert bitmap too, so colours won't change;
                                check = true;
                                break; //as we got a match, we stop checking, this colour is on the table
                            }

                        }

                        if (!check) //did we got a new colour
                        {
                            //no mathces, add to list
                            maxk++;
                            listBox1.Items.Add(maxk + ") " + x.ToString() + "," + ((y * 8) + z).ToString() + "=" + mlt[x, (y * 8) + z]);
                            if (maxk > 127)
                            {

                                listBox1.Items.Add("ERROR!!!");
                                return;
                            }
                            used[maxk] = (byte)mlt[x, (y * 8) + z];

                        }
                    }
                }
            }



            //ok, table is ok,  We minimized the colour count. 
            // now, check and reinitialize clashes.

            detectstandards(); //detects and marks attr cells don't need any multicolour time

            // first pass is done, colour table is generated. Now check for empty bitmap area;
            // empty bitmap area is rather 0 or 255 in value. Thus shows only ONE colour.
            // we always try to match this colour to upper one, so we don't have to poke the value anymore.

            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 32; x++)
                {



                    if (!clash[x, y]) //is this cell clash cell?
                    {
                        //this is a mlt cell, let's see if we can fix this
                        bool fail = false;
                        int[] shown = new int[8];
                        int[] atr = { 99, 99 };
                        int pointer = -1; //reset table pointer

                        //this is a multicolour cell. Check if we got blanks
                        //first we populate  atr table first then do the rest



                        for (int z = 0; z < 8; z++)
                        {

                            r = getattr((byte)mlt[x, (y * 8) + z]); //get color

                            #region advanced clash fixer

                            if ((bmp[x, (y * 8) + z] == 0) || (bmp[x, (y * 8) + z] == 255)) //blank cell
                            {
                                //we got a match. let's see what it really shows


                                if (bmp[x, (y * 8) + z] == 255) //buffer needed color
                                {
                                    shown[z] = r[0];
                                }
                                else
                                {
                                    shown[z] = r[1];
                                }

                                //checking now
                                fail = false;
                                for (int k = 0; k <= pointer; k++)
                                {
                                    if (atr[k] != shown[z])
                                    {
                                        // tutmadı, slot var mı?
                                        fail = true;
                                    }
                                    else
                                    {
                                        // we got it, break!
                                        fail = false;
                                        break;
                                    }
                                }

                                if (fail || (pointer == -1)) //test failed or no slots yet
                                {
                                    //check if more slots for color
                                    pointer++;
                                    if (pointer > 1)
                                    {
                                        //we are out of colour.
                                        fail = true;
                                        break;
                                    }
                                    else
                                    {
                                        //checkBox1 for different brights first this is not possible to fix:
                                        if ((pointer == 1) && ((atr[0] - 8 == shown[z]) || (atr[0] + 8 == shown[z])))
                                        {
                                            //two different brights
                                            //sorry no go!
                                            fail = true;
                                            break;
                                        }
                                        //we still got an empty slot
                                        //add to list
                                        atr[pointer] = shown[z];
                                        fail = false;

                                    }
                                }
                                if (fail) break;

                            }
                            else
                            {
                                //this is a mixed bitmap! Correct it if possible;


                                if (r[0] == r[1]) //let's see if ink&paper colours are same
                                {
                                    //this bitmap shows nothing
                                    shown[z] = r[0];

                                    //checking now
                                    fail = false;
                                    for (int k = 0; k <= pointer; k++)
                                    {
                                        if (atr[k] != shown[z])
                                        {
                                            // tutmadı, slot var mı?
                                            fail = true;
                                        }
                                        else
                                        {
                                            // we got it, break!
                                            fail = false;
                                            break;
                                        }
                                    }

                                    if (fail || (pointer == -1)) //test failed or no slots yet
                                    {
                                        //check if more slots for color
                                        pointer++;
                                        if (pointer > 1)
                                        {
                                            //we are out of colour.
                                            fail = true;
                                            break;
                                        }
                                        else
                                        {
                                            //we still got an empty slot
                                            //add to list
                                            atr[pointer] = shown[z];
                                            fail = false;

                                        }
                                    }
                                    if (fail) break;


                                }
                                else
                                {
                                    //we got two colours here.
                                    //just check both of them
                                    //checking now
                                    shown[z] = r[0];
                                    fail = false;
                                    for (int k = 0; k <= pointer; k++)
                                    {
                                        if (atr[k] != shown[z])
                                        {
                                            // tutmadı, slot var mı?
                                            fail = true;
                                        }
                                        else
                                        {
                                            // we got it, break!
                                            fail = false;
                                            break;
                                        }
                                    }

                                    if (fail || (pointer == -1)) //test failed or no slots yet
                                    {
                                        //check if more slots for color
                                        pointer++;
                                        if (pointer > 1)
                                        {
                                            //we are out of colour.
                                            fail = true;
                                            break;
                                        }
                                        else
                                        {
                                            //we still got an empty slot
                                            //add to list
                                            atr[pointer] = shown[z];
                                            fail = false;

                                        }
                                    }
                                    if (fail) break;

                                    //---now second colour---

                                    //checking now
                                    shown[z] = r[1];
                                    fail = false;
                                    for (int k = 0; k <= pointer; k++)
                                    {
                                        if (atr[k] != shown[z])
                                        {
                                            // tutmadı, slot var mı?
                                            fail = true;
                                        }
                                        else
                                        {
                                            // we got it, break!
                                            fail = false;
                                            break;
                                        }
                                    }

                                    if (fail || (pointer == -1)) //test failed or no slots yet
                                    {
                                        //check if more slots for color
                                        pointer++;
                                        if (pointer > 1)
                                        {
                                            //we are out of colour.
                                            fail = true;
                                            break;
                                        }
                                        else
                                        {
                                            //we still got an empty slot
                                            //add to list
                                            atr[pointer] = shown[z];
                                            fail = false;

                                        }
                                    }
                                    if (fail) break;
                                    //if we are still alive, it's ok!

                                }


                            }
                            #endregion advanced clash fixer



                        }


                        //ok let's see if we failed :)
                        if (!fail)
                        {
                            //we got through!! We got an clash cell! Hurray.
                            //Now fix it!
                            //first prepare our attribute!
                            if (pointer == 0) atr[1] = 0;
                            int a = putattr(atr[0], atr[1]);

                            //now invert if necessary
                            for (int z = 0; z < 8; z++)
                            {
                                r = getattr((byte)mlt[x, (y * 8) + z]);
                                mlt[x, (y * 8) + z] = a;
                                if (bmp[x, (y * 8) + z] == 255)
                                {
                                    if (atr[1] == r[0])
                                    {
                                        //fix

                                        //invert bitmap
                                        bmp[x, (y * 8) + z] ^= 0xff;
                                    }


                                }
                                else if (bmp[x, (y * 8) + z] == 0)
                                {
                                    if (atr[0] == r[1])
                                    {
                                        //fix

                                        //invert bitmap
                                        bmp[x, (y * 8) + z] ^= 0xff;
                                    }
                                }
                                else
                                {
                                    //twocolourcell
                                    if (atr[0] != r[0])
                                    {
                                        //fix

                                        //invert bitmap
                                        bmp[x, (y * 8) + z] ^= 0xff;
                                    }
                                }

                            }

                            clash[x, y] = true;
                            checkstandards(x, y * 8);

                        }
                        else
                        {
                            //we failed, so we try to make it beatiful as possible
                            //sortcell(x, y); 
                        }


                    }
                }
            }

            setup();
        }

        private void fixcell(int x, int y)
        {

            //this take a clash cell and tries to REPAIR attributes by inverting them, if possible

            for (int z = 0; z < 8; z++) //scan from top to bottom
            {

                if ((bmp[x, (y * 8) + z] == 0) || (bmp[x, (y * 8) + z] == 255)) //if bitmap is blank (either 0 or 255)
                {
                    bool done = false;


                    if ((z > 0) && (makesame(x, (y * 8) + z, -1)))
                    {
                        //ok done, we matched this colour with upper one
                        done = true;
                    }

                    if (!done)
                    {
                        if (z < 7)
                        {
                            if (makesame(x, (y * 8) + z, 1))
                            {
                                //well we take the bottom colour.
                                done = true;
                            }
                        }
                    };


                    //done, move to next one.

                }
            }
        }

        private bool makesame(int x, int y, int z)
        {
            //z is -1 or 1, to check upper or lower cell
            int[] r = new int[4];
            int[] s = new int[4];
            int shown;
            int k = 0;
            r = getattr((byte)mlt[x, y]); //get mlt color

            #region checkmatch
            if (bmp[x, y] == 255) //if full of 1's, it's pure ink
            {
                shown = 0; //ink

            }
            else
            {
                shown = 1; //paper
            }

            //BLACK CHECK

            if (r[shown] == 0) //if shown colour is black, it doesn't matter if it's bright or dim
            {
                k = 8;
            }

            else if (r[shown] == 8)
            {
                k = 0;
            }
            else
            {
                //other colors has no spare;
                k = r[shown];
            }
            //end of black check

            //now we check the next one.
            s = getattr((byte)mlt[x, y + z]); //get mlt color of next cell
            if ((r[shown] == s[0]) || (r[shown] == s[1]) || (k == s[0]) || (k == s[1])) //check if next cell has one of our colours
            {
                //yes it is
                //now convert to it.
                if (r[shown] == s[0])
                {
                    //ink match
                    if (shown != 0) //are we already showing ink?
                    {
                        //no we are showing paper
                        bmp[x, y] = 255; //make it shows ink

                    }
                }
                else if (r[shown] == s[1])
                {
                    //paper match
                    if (shown != 1) //are we already showing paper?
                    {
                        //no we are showing paper
                        bmp[x, y] = 0; //make it shows ink

                    }
                }
                else if (k == s[1])
                {
                    //Black paper match
                    if (shown != 1) //are we already showing paper?
                    {
                        //no we are showing paper
                        bmp[x, y] = 0; //make it shows ink

                    }
                }
                if (k == s[0])
                {
                    //black ink match
                    if (shown != 0) //are we already showing ink?
                    {
                        //no we are showing paper
                        bmp[x, y] = 255; //make it shows ink

                    }
                }
                //either way, paper or ink match, we'll devour next colour
                mlt[x, y] = mlt[x, y + z]; //get next colour, which matches ink
                return true;
            }
            #endregion

            return false; //no match!!!
        }

        /// <summary>
        /// Adds color to the table
        /// </summary>
        /// <param name="table">the table array to be modified</param>
        /// <param name="item">item to be added to table</param>
        /// <returns>0 if table is full, 1 if item already exists, 2 if a new item added</returns>
        private int addtotable(ref int[] table, int item)
        {
            int found = 0;
            if (item == 8) item = 0; //ignore black brightness
            if ((table[2] == 0) && (((item + 8 == table[0]) || (item - 8 == table[0])))) //brightness check
            {
                table[1] = 0;
                return 0;

            }
            for (int x = 0; x <= table[2]; x++)
            {
                if (table[x] == item) found = 1;
            }
            if ((found == 0) && (table[2] < 1))
            {
                table[2]++;
                table[table[2]] = item;
                found = 2;
            }


            return found;
        }

        /// <summary>
        /// inverts a range of bytes
        /// </summary>
        /// <param name="color">forced color</param>
        /// <param name="x">coord</param>
        /// <param name="y">mlt coord</param>
        /// <param name="z">length</param>
        private void sortuntil(int color, int x, int y, int z)
        {
            int[] r = getattrignore((byte)color);

            if ((y == 112) && (x == 9))
            {
                int debug = 0;
            }
            for (int k = y; k <= y + z; k++)
            {
                int[] s = getattrignore((byte)mlt[x, k]);

                if (bmp[x, k] == 0)
                {
                    //paper match
                    if (r[0] == s[1]) //if paper matches ink
                    {
                        //make it ink
                        bmp[x, k] = 255;
                    }
                    mlt[x, k] = color;
                }
                else if (bmp[x, k] == 255)
                {
                    //ink match
                    if (s[0] == r[1])
                    {
                        //inverted
                        bmp[x, k] = 0; //make it paper
                    }
                    mlt[x, k] = color;
                }
                else
                {
                    //two colors
                    if (s[0] == r[1])
                    {

                        //inverted
                        bmp[x, k] ^= 255;
                        mlt[x, k] = color;
                    }
                }

            }
        }

        private void sortcell(int x, int y)
        {

            y = y * 8;
            int[] table = { 99, 99, -1 };
            int[] r = new int[3];

            if (r[0] == 8) r[0] = 0;
            if (r[1] == 8) r[1] = 0;

            int starty = 0;

            for (int z = 0; z < 8; z++)
            {
                r = getattr((byte)mlt[x, y + z]);
                if (bmp[x, y + z] == 0)
                {

                    int c = addtotable(ref table, r[1]);
                    if (c == 0)
                    {
                        //table full, color not found, write pixels, clear table start new
                        sortuntil(putattr(table[0], table[1]), x, y + starty, (z - starty) - 1);
                        starty = z;
                        table[0] = 99;
                        table[1] = 99;
                        table[2] = -1;
                        addtotable(ref table, r[1]);
                    }
                }
                else if (bmp[x, y + z] == 255)
                {
                    int c = addtotable(ref table, r[0]);
                    if (c == 0)
                    {
                        //table full, color not found, write pixels, clear table start new
                        sortuntil(putattr(table[0], table[1]), x, y + starty, (z - starty) - 1);
                        starty = z;
                        table[0] = 99;
                        table[1] = 99;
                        table[2] = -1;
                        addtotable(ref table, r[0]);
                    }

                }
                else
                {
                    int c = addtotable(ref table, r[0]);
                    if (c == 0)
                    {
                        //table full, color not found, write pixels, clear table start new
                        sortuntil(putattr(table[0], table[1]), x, y + starty, (z - starty) - 1);
                        starty = z;
                        table[0] = 99;
                        table[1] = 99;
                        table[2] = -1;
                        addtotable(ref table, r[0]);
                        addtotable(ref table, r[1]);


                    }
                    else
                    {


                        c = addtotable(ref table, r[1]);
                        if (c == 0)
                        {
                            //table full, color not found, write pixels, clear table start new
                            sortuntil(putattr(table[0], table[1]), x, y + starty, (z - starty) - 1);
                            starty = z;
                            table[0] = 99;
                            table[1] = 99;
                            table[2] = -1;
                            addtotable(ref table, r[1]);

                        }
                    }

                }
            }
            sortuntil(putattr(table[0], table[1]), x, y + starty, (7 - starty));

        }

        private cpu emulateraster(string code, cpu c)
        {
            reg = c;
            reg.cycle = 0;
            using (StringReader reader = new StringReader(code))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {

                    reg.cycle = execop(line, reg.cycle);

                }
            }

            return reg;
        }

        // OBJECTS =====================================================================================================

        private void button8_Click(object sender, EventArgs e)
        {
            startemulation();
        }

        private void startemulation()
        {
            if (codeLineIndex == 0) lastcomputedmarks = false;
            button9.Text = "Stop";
            textBox14.Text = "Emulating...";
            Application.DoEvents();
            codeLineIndex = emulatenow(codeLineIndex);
            
        }

        private int emulatenow(int startline=0)
        {
            //Start Emulation
            button8.Enabled = false;
            emulationactive = false;
            if (checkVERBOSE.Checked) listBox2.Items.Clear();
            if (checkCLEAR.Checked)
            {
                buffy = new Bitmap(32 * cw, 192 * ch);
                Graphics G = Graphics.FromImage(buffy);

                G.Clear(rg[0]);
                pictureBox1.Image = buffy;
                G.Dispose();
            }
            reg.sp = 32767;
            reg.pc = 32768;
            ld("bc", 0);
            ld("hl", 0);
            ld("de", 0);
            exx();
            ld("bc", 0);
            ld("hl", 0);
            ld("de", 0);
            reg.a = 0;
            reg.i = 254;

            //int endemul48 = 57243;
            //int endemul128 = 58033;
            //emulation ends when JP BONG is acquired by emulator.

            //copy screen 
            int t = 22528;
            for (int y = 0; y < 192; y += 8)
            {
                for (int x = 0; x < 32; x++)
                {
                    ram[t] = mlt[x, y];
                    t++;
                }
            }

            //now do the code
            string code = textBox7.Text;
            if (code.Trim() == "")
            {
                textBox14.Text = "Regenerating...";
                Application.DoEvents();
                regenerate();
                Application.DoEvents();
                code = textBox7.Text;
                
            }
            bool brake=false;
            int lineIndex = 0;
            using (StringReader reader = new StringReader(code))
            {
                
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Skip lines until we reach the stored index
                    if (lineIndex < startline)
                    {
                        //skip this line
                    }
                    else
                    {
                         brake = StepSingleLine(line);
                        txtLastCodeLine.Text = lineIndex.ToString();
                        if (brake) break;
                    }
                    lineIndex++;
                }
            }
            emulationactive = false;
            button8.Enabled = true;
            button9.Text = "Clear Marks";
            if (checkMARK.Checked) lastcomputedmarks = true;
            textBox14.Text = "Emulation completed.";
            
            return lineIndex;

        }  //START EMULATION


        private bool StepSingleLine(string line)
        {
            if (line.Contains("jp bong"))
            {
                emulationactive = false;
                return true;
            }

            if (line.Contains("Raster Start"))
            {
                emulationactive = true;
                reg.cycle = Convert.ToInt32(textBox12.Text);
                lastulats = reg.cycle;
            }

            if (emulationactive)
            {

                reg.cycle = execop(line, reg.cycle);
                if (checkVERBOSE.Checked)
                {
                    //time match check
                    if (line.Contains(";:::"))
                    {
                        string[] s = line.Split('*');
                        emuCurrentRaster = Convert.ToInt32(s[1].Trim());
                    }

                    if ((line.Length > 5) && (line.IndexOf(';') < 0))
                    {
                        if (line.Substring(0, 6) == "ld sp,")
                        {
                            /*if (Convert.ToInt32(line.Substring(6)) != reg.cycle)
                            {
                                hScrollBar1.Value = reg.cycle;
                                emulationactive = false;
                            }*/
                        }
                    }

                    if (line.Contains("Raster"))
                    {
                        hScrollBar1.Value = reg.cycle;
                    }
                    updateregs();
                    Thread.Sleep(trackBar1.Value);
                    Application.DoEvents();
                }

                if (!emulationactive) return true;

            }

            return false;
        }

        private void pictureBox1_SizeChanged(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FileInfo test = new FileInfo(Application.StartupPath + "\\pasmo.exe");
            if (!test.Exists)
            {
                button6.Text = "Save TAP(Pasmo?)";
                button6.Enabled = false;
            };

            LoadRecentFiles();


            // Update ComboBox5 collection based on available packers
            comboBox5.Items.Clear();
            comboBox5.Items.Add("none");

            test = new FileInfo(Application.StartupPath + "\\zx0.exe");
            if (test.Exists)
            {
                comboBox5.Items.Add("zx0");
                comboBox5.SelectedIndex = 1;
            }
            else
            {
                comboBox5.Items.Add("zx0 not found");
                comboBox5.SelectedIndex = 0;
            }

            
            test = new FileInfo(Application.StartupPath + "\\zx7.exe");
            if (test.Exists)
            {
                comboBox5.Items.Add("zx7");
                if (comboBox5.SelectedIndex == 0) comboBox5.SelectedIndex = 2;
            }
            else
            {
                comboBox5.Items.Add("zx7 not found");
            }

            test = new FileInfo(Application.StartupPath + "\\MegaLZ.exe");
            if (test.Exists)
            {
                comboBox5.Items.Add("mlz");
                if (comboBox5.SelectedIndex == 0) comboBox5.SelectedIndex = 3;
            }
            else
            {
                comboBox5.Items.Add("mlz not found");
            }



            int hival = 235;
            int lowval = 202;

            setPalette(hival,lowval);


            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 24; y++)
                {
                    attr[x, y, 8] = 7;
                    clash[x, y] = true;
                }
            }
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 192; y++)
                {

                    mlt[x, y] = 6;
                }
            }

            comboBox2.Items.Clear();
            comboBox2.Items.Add(contmodel[0, 0]);
            comboBox2.Items.Add(contmodel[1, 0]);
            comboBox2.Items.Add(contmodel[2, 0]);
            comboBox2.SelectedIndex = 0;
            comboBox3.SelectedIndex = 1;
            tabControl1.SelectedIndex = 0;
            comboBox4.Items.Clear();
            for (int x = 0; x < 192; x++)
                comboBox4.Items.Add(x);

            //readtable(); idea is rejected
            resetmodel();

            timer1.Enabled = true;

            //MessageBox.Show("V0.6 - Work in progress\r\n\r\nDo not spread\r\n\r\nRead: Help > Usage for licence and instructions","WARNING!");
            textBox1.Focus();


        }


        private void setPalette(int hival, int lowval)
        {

            rg[0] = Color.FromArgb(255, 0, 0, 0);
            rg[1] = Color.FromArgb(255, 0, 0, lowval);
            rg[2] = Color.FromArgb(255, lowval, 0, 0);
            rg[3] = Color.FromArgb(255, lowval, 0, lowval);
            rg[4] = Color.FromArgb(255, 0, lowval, 0);
            rg[5] = Color.FromArgb(255, 0, lowval, lowval);
            rg[6] = Color.FromArgb(255, lowval, lowval, 0);
            rg[7] = Color.FromArgb(255, lowval, lowval, lowval);

            rg[8] = Color.FromArgb(255, 20, 20, 20);
            rg[9] = Color.FromArgb(255, 0, 0, hival);
            rg[10] = Color.FromArgb(255, hival, 0, 0);
            rg[11] = Color.FromArgb(255, hival, 0, hival);
            rg[12] = Color.FromArgb(255, 0, hival, 0);
            rg[13] = Color.FromArgb(255, 0, hival, hival);
            rg[14] = Color.FromArgb(255, hival, hival, 0);
            rg[15] = Color.FromArgb(255, hival, hival, hival);
            rg[16] = Color.FromArgb(128, hival, 180, 180);
            rg[17] = Color.FromArgb(128, 180, hival, 200);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //loader("C:\\Users\\beyu\\Documents\\Visual Studio 2008\\Projects\\aSMP\\MiamiFrank.mlt");
            if (dosyam != "")
            {
                loader(dosyam);

                detectstandards();


            }
            comboBox1.SelectedIndex = 4;
            timer1.Enabled = false;

        }

        private void hScrollBar1_Scroll(object sender, ScrollEventArgs e)
        {
            textBox8.Text = hScrollBar1.Value.ToString();
            cycle = hScrollBar1.Value;
            textBox11.Text = contention(cycle).ToString();

        }

        private void pictureBox2_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bright = (e.Y > 15 ? 1 : 0);
                ink = (byte)(e.X / (16));

            }
            if (e.Button == MouseButtons.Right)
            {
                bright = (e.Y > 15 ? 1 : 0);
                paper = (byte)(e.X / (16));

            }

            

            showpalet();
            drawpalet(); 
            

        }

        private void textBox8_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                hScrollBar1.Value = Convert.ToInt32(textBox8.Text);
            }
        }

        private void hScrollBar1_ValueChanged(object sender, EventArgs e)
        {

            cycle = hScrollBar1.Value;
            int y = (cycle - contst) / modelts;
            int x = ((cycle - (contst + (y * modelts))) / 4);
            textBox8.Text = hScrollBar1.Value.ToString();// +"X:" + x + " Y:" + y;
            //setup();
            int ncycle = (cycle - rasterst);
            int raster = ncycle / modelts;
            raster = raster * modelts;
            int noncont = raster + 128;
            int nextcont = raster + modelts;
            int delay = contention(cycle);
            textBox11.Text = delay.ToString();
            if ((ncycle >= noncont) && (ncycle < nextcont))
            {
                //raster is in the border
                textBox8.BackColor = Color.LimeGreen;
            }
            else if (ncycle >= raster && ncycle < noncont)
            {
                //reading memory
                setup();
                if (delay > 0)
                {
                    textBox8.BackColor = Color.Orange;
                }
                else
                {
                    textBox8.BackColor = Color.Gold;
                }
            }
            else
            {
                //out of scan
                textBox8.BackColor = Color.Yellow;
            }


        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "Go")
            {

                button1.Text = "Stop";
                timer2.Enabled = true;

            }
            else
            {
                button1.Text = "Go";
                timer2.Enabled = false;

            }

        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if (hScrollBar1.Value == hScrollBar1.Maximum)
            {
                hScrollBar1.Value = hScrollBar1.Minimum;

            }
            hScrollBar1.Value++;

        }

        private void textBox7_MouseClick(object sender, MouseEventArgs e)
        {

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            optShowMLTadresses = checkBox3.Checked;
            setup();
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }

        private void chkShowAttribs_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }
        private void button2_Click(object sender, EventArgs e)
        {
            fixattr();

        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }

        private void checkBox6_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    sortcell(x, y);

                }
            }
            fixattr();
            setup();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            density = comboBox1.SelectedIndex + 1;
            setup();
        }


        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            // Check if the mouse is over the ComboBox
           
                if (comboBox1.Items.Count > 0)
                {
                    // Get the current selected index
                    int currentIndex = comboBox1.SelectedIndex;

                    // Determine the direction of the scroll
                    int delta = e.Delta > 0 ? -1 : 1; // Scroll up -> previous item, scroll down -> next item

                    // Calculate the new index
                    int newIndex = (currentIndex + delta) % comboBox1.Items.Count;

                    // Handle wrap-around
                    if (newIndex < 0)
                        newIndex = comboBox1.Items.Count - 1;

                    // Set the new selected index
                    comboBox1.SelectedIndex = newIndex;
                }
            
        }


        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            
            /*
            if (e.Button == MouseButtons.Middle)
            {
                pictureBox1.Left += (e.X - px);
                pictureBox1.Top += (e.Y - py);
            }
            else 
                */
            if (radioButton4.Checked || optDraggingNow)
            {
                if (e.Button == MouseButtons.Left)
                {
                    pictureBox1.Left += (e.X - px);
                    pictureBox1.Top += (e.Y - py);
                }
            }
            else
            {
                move_cursor(e.X, e.Y, (int)e.Button);
            }
            

        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (!(radioButton4.Checked || optDraggingNow)) move_cursor(e.X, e.Y, (int)e.Button);

            if (e.Button == MouseButtons.Left)
            {
                isHeldDown = true;
                px = e.X;
                py = e.Y;

            }
        }

        private void button4_Click(object sender, EventArgs e)
        {


        }

        private void button9_Click(object sender, EventArgs e)
        {
            if (emulationactive)
            {
                emulationactive = false;
                button9.Text = "Clear Marks";
            }
            else
            {
                codeLineIndex = 0;
                txtLastCodeLine.Text = "0";

                button9.Text = "Emulation Ended.";
                lastcomputedmarks = false;
            }

            setup();
        }

        private void button10_Click(object sender, EventArgs e)
        {
            regenerate();
        }

        private void regenerate()
        {
            if (Convert.ToInt32(textTOP.Text) > 22) textTOP.Text = "22";
            if (Convert.ToInt32(textTOP.Text) < 0) textTOP.Text = "0";
            if (Convert.ToInt32(textBOTTOM.Text) > 23) textBOTTOM.Text = "23";
            if (Convert.ToInt32(textBOTTOM.Text) < 0) textBOTTOM.Text = "0";

            button10.Enabled = false;
            textBox7.Text = generatecode();
            button10.Enabled = true;

        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (textBox7.Text != "") Clipboard.SetText(textBox7.Text);
        }

        private void button12_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                button12.BackColor = colorDialog1.Color;
                mltcol = colorDialog1.Color;
                rg[16] = colorDialog1.Color;
            }


        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Alt) 
            {

                ToolTip tip = new ToolTip();
                tip.Show(getcodeinfo(cx, cy), this, (cx + 1) * cw, cy * ch, 6000);

            }

            if (e.KeyCode == Keys.G)
            {
                checkBox1.Checked = !checkBox1.Checked;
            }

            if (e.KeyCode == Keys.M)
            {
                checkBox3.Checked = !checkBox3.Checked;
            }



            if (e.KeyCode == Keys.L)
            {
                checkLINE.Checked = !checkLINE.Checked;
            }

            if (e.KeyCode == Keys.P)
            {
                radioButton6.Checked = true;
            }

            if (e.KeyCode == Keys.Z)
            {
                comboBox1.Focus();
            }

            if (e.KeyCode == Keys.R)
            {
                pictureBox1.Left = 12;
                pictureBox1.Top = 30;
            }

            if (e.KeyCode == Keys.U)
            {
                emulatenow();
            }

            if (e.KeyCode == Keys.N)
            {
                regenerate();
            }

            if (e.KeyCode == Keys.Space)
            {
                pictureBox1.Focus();
                if (radioButton4.Checked == true)
                {
                    radioButton4.Checked = false;
                    radioButton3.Checked = true;

                }
                else
                {
                    radioButton4.Checked = true;
                    radioButton3.Checked = false;
                }

            }

            if (e.Control && !optShowMLTadresses) // Check if Ctrl is pressed and state is not already set
            {
                optShowMLTadresses = true;
                setup();
            }

            if (e.Alt && !optDraggingNow) // Check if Ctrl is pressed and state is not already set
            {
                optDraggingNow = true;
                pictureBox1.Cursor = Cursors.Hand;
            }

            if (radioButton10.Checked )
            {
                if (e.Shift)
                {
                    if (pictSpare == null)
                    {
                        ShowSwapBuffer();  // Function to create and display pictSpare
                    }

                }
               

            }
        

        }

        private void button13_Click(object sender, EventArgs e)
        {
            listBox2.Items.Clear();

        }

        private void button14_Click(object sender, EventArgs e)
        {
            string[] s = label18.Text.Split(',');
            int y = Convert.ToInt32(s[1]);
            int x = Convert.ToInt32(s[0]);
            bmp[x, y] = Convert.ToByte(textBMP.Text, 2);
            setup();

        }

        private void button18_Click(object sender, EventArgs e)
        {



        }

        private void button19_Click(object sender, EventArgs e)
        {

        }

        private void button7_Click(object sender, EventArgs e)
        {

        }

        private void button5_Click(object sender, EventArgs e)
        {

        }

        private void button15_Click(object sender, EventArgs e)
        {


        }

        private void button20_Click(object sender, EventArgs e)
        {

        }

        private void button21_Click(object sender, EventArgs e)
        {


        }

        private void button22_Click(object sender, EventArgs e)
        {

        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            setcontentionmodel(comboBox2.SelectedIndex);
            if (comboBox2.SelectedIndex == 0)
            {
                if (chkUseBank7.Checked == true) chkUseBank7.CheckState = CheckState.Indeterminate;
                chkUseBank7.Enabled = false;
            }
            else
            {
                if (chkUseBank7.CheckState == CheckState.Indeterminate)
                {
                    chkUseBank7.Checked = true;
                    chkUseBank7.CheckState = CheckState.Checked;
                }
                chkUseBank7.Enabled = true;
            }

        }

        private void button23_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            label13.Text = "Slowdown: " + trackBar1.Value;
        }

        private void button24_Click(object sender, EventArgs e)
        {
            textBMP.Text = Convert.ToString((Convert.ToByte(textBMP.Text, 2) ^ 255), 2).PadLeft(8, '0');
        }

        private void textBOTTOM_TextChanged(object sender, EventArgs e)
        {

        }

        private void textTOP_TextChanged(object sender, EventArgs e)
        {

        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton4.Checked)
            {
                pictureBox1.Cursor = Cursors.Hand;
            }
            else
            {
                pictureBox1.Cursor = Cursors.Default;
            }
        }

        private void checkLINE_CheckedChanged(object sender, EventArgs e)
        {
            setup();

        }

        private void button25_Click(object sender, EventArgs e)
        {
            pictureBox1.Focus();
        }

        private void button25_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {

            if (e.Alt)
            {
                for (int x = 0; x < 32; x++)
                {
                    for (int y = 0; y < 24; y++)
                    {
                        attr[x, y, 8] = 7;
                        clash[x, y] = true;
                    }
                }
                for (int x = 0; x < 32; x++)
                {
                    for (int y = 0; y < 192; y++)
                    {

                        mlt[x, y] = 7;
                        bmp[x, y] = 0;
                        chex[x, y] = 0;
                    }
                }
                resetmodel();
                setup();
                textBox7.Text = "";
            }


        }

        private void button6_Click(object sender, EventArgs e)
        {
            exportTap();

        }
        private void exportTap()
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Tape File(*.tap)|*.tap";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                FileInfo test = new FileInfo(Application.StartupPath + "\\pasmo.exe");
                if (!test.Exists)
                {
                    MessageBox.Show("Pasmo is not found.\r\nPlease put pasmo.exe in the same program folder.","No Pasmo.exe?!",MessageBoxButtons.OK);
                    
                };

                string filename = save.FileName + ".asm";
                string f = textBox7.Text;
                using (BinaryWriter b = new BinaryWriter(File.Open(filename, FileMode.Create)))
                {
                    // 3. Use foreach and write all 12 integers.
                    foreach (byte i in f)
                    {
                        b.Write(i);
                    }
                }

                //txt tamam
                // Start the child process.
                System.Diagnostics.Process p = new System.Diagnostics.Process();

                // Redirect the output stream of the child process.
                p.StartInfo.UseShellExecute = false;
                
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = Application.StartupPath + "\\pasmo.exe";
                p.StartInfo.Arguments = " -v --tapbas \"" + filename + "\" \"" + save.FileName +"\" ";
                //p.StartInfo.UseShellExecute = true;
                //p.StartInfo.CreateNoWindow = true;
                MessageBox.Show(p.StartInfo.FileName + p.StartInfo.Arguments);
                p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
                p.Start();
                while (!p.StandardOutput.EndOfStream)
                {
                    listBox2.Items.Clear();
                    listBox2.Items.Add(p.StandardOutput.ReadLine());
                    listBox2.TopIndex = listBox2.Items.Count - 1;
                }
                p.WaitForExit();

                /*FileInfo fi = new FileInfo(save.FileName);
                if (fi.Exists)
                {

                }*/


            }
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void ımportToSCRBufferToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog save = new OpenFileDialog();
            save.Filter = "SCR RAW Progressive Multicolor Files (*.scr)|*.scr|Raw Bitmap files (bin.*)|bin.*|All files (*.*)|*.*";
            save.FilterIndex = 1;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                dosyam = save.FileName;
                loader(dosyam, 13);
                setup();
            }
        }

        private void copyToMLTBufferToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {

                    umlt[x, y] = mlt[x, y];
                    ubmp[x, y] = bmp[x, y];

                }
            }
        }

        private void mLTSCREENToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    attr[x, y / 8, 9] = 1;

                    mlt[x, y] = umlt[x, y];
                    bmp[x, y] = ubmp[x, y];

                }
            }
            setup();
        }

        private void pasteToSCRBufferToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    attr[x, y / 8, 9] = 1;

                    mlt[x, y] = spare[x, y / 8];
                    bmp[x, y] = sparebmp[x, y];

                }
            }
            setup();
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 24; y++)
                {
                    attr[x, y, 8] = 7;
                    clash[x, y] = true;
                }
            }
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 192; y++)
                {

                    mlt[x, y] = 7;
                    bmp[x, y] = 0;
                    chex[x, y] = 0;
                }
            }
            resetmodel();
            setup();
            textBox7.Text = "";
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog save = new OpenFileDialog();
            save.Filter = "MLT RAW Progressive Multicolor Files (*.mlt)|*.mlt|12288+ byte Extended SCR Files (*.scr)|*.scr|All files (*.*)|*.*";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                dosyam = save.FileName;
                loader(dosyam);

                detectstandards();
                lastcomputedmarks = false;

                setup();

                textBox7.Text = "";

                // recent listesine ekle
                if (recentFiles.Contains(dosyam))
                    recentFiles.Remove(dosyam);
                recentFiles.Insert(0, dosyam);
                if (recentFiles.Count > maxRecent)
                    recentFiles.RemoveAt(recentFiles.Count - 1);

                UpdateRecentMenu();
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Progressive Multicolour File(*.mlt)|*.mlt|All files (*.*)|*.*";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                string filename = save.FileName;
                byte[] f = packbitmap();
                byte[] m = packmlt();
                byte[] all = new byte[f.Length + m.Length];
                Array.Copy(f, all, f.Length);
                Array.Copy(m, 0, all, f.Length, m.Length);

                export(filename, all);
            }


        }

        private void LoadRecentFiles()
        {
            if (File.Exists(recentFilePath))
            {
                recentFiles = File.ReadAllLines(recentFilePath).ToList();
            }
            UpdateRecentMenu();
        }
        private void SaveRecentFiles()
        {
            File.WriteAllLines(recentFilePath, recentFiles);
        }
        private void UpdateRecentMenu()
        {
            recentyOpenedFilesToolStripMenuItem.DropDownItems.Clear();

            // Clear butonunu ekle
            recentyOpenedFilesToolStripMenuItem.DropDownItems.Add(clearRecentsToolStripMenuItem);
            recentyOpenedFilesToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

            foreach (var file in recentFiles)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(file);
                item.Click += (s, e) =>
                {
                    if (File.Exists(file))
                    {
                        dosyam = file;
                        loader(dosyam);
                        detectstandards();
                        lastcomputedmarks = false;
                        setup();
                        textBox7.Text = "";
                    }
                    else
                    {
                        MessageBox.Show("File not found: " + file);
                    }
                };
                recentyOpenedFilesToolStripMenuItem.DropDownItems.Add(item);
            }
        }
        private void clearRecentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            recentFiles.Clear();
            UpdateRecentMenu();
        }




        private void bitmapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog save = new OpenFileDialog();
            save.Filter = "SCR RAW Progressive Multicolor Files (*.scr)|*.scr|Raw Bitmap files (bin.*)|bin.*|All files (*.*)|*.*";
            save.FilterIndex = 1;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                dosyam = save.FileName;
                loader(dosyam, 12);
                setup();
            }
        }

        private void sCRToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog save = new OpenFileDialog();
            save.Filter = "SCR RAW Progressive Multicolor Files (*.scr)|*.scr|All files (*.*)|*.*";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                dosyam = save.FileName;
                loader(dosyam, 11);

            }
        }

        private void button16_Click(object sender, EventArgs e)
        {
            OpenFileDialog save = new OpenFileDialog();
            save.Filter = "768 byte raw binary Files (*.bin)|*.bin|All files (*.*)|*.*";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                dosyam = save.FileName;
                //loader(dosyam, 11);  //TODO : force load attribs

            }
        }

        private void button17_Click(object sender, EventArgs e)
        {

        }

        private void mLTToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog save = new OpenFileDialog();
            save.Filter = "MLT RAW Progressive Multicolor Files (*.mlt)|*.mlt|All files (*.*)|*.*";
            save.FilterIndex = 1;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                dosyam = save.FileName;
                loader(dosyam);


                detectstandards();



                //textBox7.Text = generatecode();

                //comboBox1.SelectedIndex = 3;
                setup();

                // pictureBox1.Left = 12;
                // pictureBox1.Top = 12;
                textBox7.Text = "";

            }
        }

        private void bitmapToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Raw binary screen bitmap buffer (*.bin)|*.bin|All files (*.*)|*.*";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                string filename = save.FileName;
                byte[] f = packbitmap();
                export(filename, f);
            }

        }

        private void sCRToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Raw binary screen bitmap buffer (*.scr)|*.scr|All files (*.*)|*.*";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                string filename = save.FileName;
                byte[] f = packbitmap();
                byte[] s = packattribs();
                byte[] all = new byte[f.Length + s.Length];
                Array.Copy(f, all, f.Length);
                Array.Copy(s, 0, all, f.Length, s.Length);

                export(filename, all);
            }
        }

        private void tapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Tape File(*.tap)|*.tap";
            save.FilterIndex = 0;
            save.RestoreDirectory = true;

            if (save.ShowDialog() == DialogResult.OK)
            {
                string filename = save.FileName + ".asm";
                string f = textBox7.Text;
                using (BinaryWriter b = new BinaryWriter(File.Open(filename, FileMode.Create)))
                {
                    // 3. Use foreach and write all 12 integers.
                    foreach (byte i in f)
                    {
                        b.Write(i);
                    }
                }

                //txt tamam
                // Start the child process.
                System.Diagnostics.Process p = new System.Diagnostics.Process();

                // Redirect the output stream of the child process.
                p.StartInfo.UseShellExecute = true;
                //p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = Application.StartupPath + "\\pasmo.exe";
                p.StartInfo.Arguments = "-v --tapbas " + save.FileName + ".asm  " + save.FileName;
                //p.StartInfo.UseShellExecute = true;
                //p.StartInfo.CreateNoWindow = true;
                p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
                p.Start();

                p.WaitForExit();

                /*FileInfo fi = new FileInfo(save.FileName);
                if (fi.Exists)
                {

                }*/


            }
        }

        private void button4_Click_1(object sender, EventArgs e)
        {
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {

                    if (button4.Text == "Bright")
                    {
                        mlt[x, y] = brighten(mlt[x, y]);
                    }
                    else
                    {
                        mlt[x, y] = debright(mlt[x, y]);
                    }

                }
            }
            setup();
            if (button4.Text == "Bright")
                    {
                        button4.Text = "De-Bright";
            }
            else
            {
                button4.Text ="Bright";
            }
        }
        private int debright(int renk)
        {
            int[] r = new int[3];
            r = getattr((byte)renk);
            if (r[2] == 8)
            {
                return putattr(r[0]-8, r[1]-8);
            }
            else
            {
                return renk;
            }
        }

        private int deflash(int renk)
        {
            int[] r = new int[4];
            r = getattrwFlash((byte)renk);
            if (r[3] == 1)
            {
                return putattr(r[0], r[1]);
            }
            else
            {
                return renk;
            }
        }

        private int brighten(int renk)
        {
            int[] r = new int[3];
            r = getattr((byte)renk);
            if (r[2] == 0)
            {
                return putattr(r[0] + 8, r[1] + 8);
            }
            else
            {
                return renk;
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void usageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form helpForm = new Form();
            helpForm.Text = "Welcome";
            helpForm.Width = 600;
            helpForm.Height = 500;

            RichTextBox rtb = new RichTextBox();
            rtb.Dock = DockStyle.Fill;
            rtb.ReadOnly = true;
            rtb.BackColor = SystemColors.Window;
            rtb.Font = new Font("Segoe UI", 10);
            #region Edit Help Text Here
            rtb.Text =
@"Asmp - a Sinclair ZX Spectrum Progressive Multicolor Screen to Assembly Code Converter

This tool analyzes a MLT file to generate an raster chasing assembly code to spread multicolor cells around the screen for optimal viewing.

INSTALL
- Put zx0, dzx0, zx7, MegaLZ.exe and Pasmo.exe into the same directory as asmp.exe (optional).

USAGE (MLT files only)
1. File > Open... to select a progressive MLT file (12288 bytes).
2. Switch to 'Edit & Fix' tab.
    Click 'Fix Attrs' and 'Re-order' buttons to minimize multicolor usage without changing the image.
    This may not change the image but optimizes it. Sometimes you do not want to use these if you made manual adjustments.
3. Switch to 'Generation Settings' tab and select target platform (48/128/+3) and other options.
4. Click 'Regenerate'.
5. To test the output, switch to 'Tests and diagnostics' tab and click 'Start Emulation'.
6. If no marked cells, switch back to 'Generation Settings' tab and click 'Export Tap' at the bottom.

LIMITATIONS
Asmp can support up to 12 cells per raster line. If there are more than 12 multicolor cells on a line, some will be marked in red (if show warnings is checked) and will not be rendered correctly in the emulation. 

On many occasions you will encounter images with more than 12 multicolor cells on a raster line. When you hower your mouse over a line it will show MLT marks around cells that use cpu processing time.

ZX Spectrum has only 224 tstates per raster line, and that part of the memory is contended. You we got actually far less then 224 tstates to process the screen.
Asmp highly optimizes the time to update multicolor cells tries to squeeze out every single tstate. But sometimes it is not enough. You have to make compromises and reduce the number of multicolor cells on a line selectively.

FIXING the IMAGE BY HAND
Fixing means mostly comprimising from multicolor and rather use data from raster above. As this is an artistic process, you have to decide which cells to change. This can't be automated.

For example if you have 15 multicolor cells on a line, and you may see some very similar colors (eg. red below and magenta above) on the line above, you may copy colors from above raster at that column giving up multicolor ability of that cell thus releasing some cpu time. Note that copying colors means you change colors in your image and a destructive process.
Or if a raster have a small number of multicolor cells, you may want to pull some of the cells from below raster to relieve load from the below raster.
So use 'Edit and Fix' tab to use 'copy from above/below' tool to delegate multicolor workload to above or from below raster.

A Warning: after manually editing the image, you may want to avoid using 'Fix Attrs' and 'Re-order' buttons as they will change your manual adjustments.

Pick: You select one single cell on the image. The cell will be highlighted with a white border. It's bitmap and attribute data will be shown. 

Pixel/Colors/P+C: You can set pixel data, update attributes or both in a cell.,

SWAP SCREEN

Asmp has a swap screen feature to make this process easier. Use a converter to generate regular SCR image of the same MLT file. Load it in the SWAP buffer.
Then you can enable a SWAP tool and copy entire character from the swap buffer to mlt buffer, removing MLT ability of the block entirely, turning into a normal SCR block.
This is purely time saving technique, as you would do the same by picking each cell and setting pixel and attributes manually.

Optionally you may want to use a secondary mlt image as swap buffer, to swap 8x1 cells if you have multiple versions of the same image, you can merge two images together selectively.

TIPS
- Edit&Fix tab has Regenarate, test and revert buttons saving you travelling around tabs to test your image. 
- Fine tune images in 'Edit & Fix' tab.
- Preferences menu: hide grid, marks, etc.
- Spacebar: drag image. Zoom: view closer.";
            #endregion
            helpForm.Controls.Add(rtb);
            helpForm.ShowDialog();
        }

        private void usageToolStripMenuItem_Click2(object sender, EventArgs e)
        {
            /*string msg = "INSTALL\r\nPut MegaLZ.exe and Pasmo.exe into same directory with asmp.exe (optional)\r\n\r\n USAGE\r\n *** This tool only converts MLT files! ***\r\n\r\n";
            msg = msg + "\r\n 1.File > Open... to select a progressive MLT file (12288 bytes)\r\n 2.Switch to Edit & Fix tab\r\n 3.Click fix attrs and Re-order buttons to optimize image (you will see nothing changed but they work)";
            msg = msg + "\r\n 3.Switch to code tab. Select your target platform for best results. \r\n 4.Press regenerate button. The code will appear.\r\n 5.Press copy code, and code will be copied into clipboard. \r\n 6. Compile the code with pasmo the assembler and run it in your favorite machine.";
            msg = msg + "\r\n\r\n -- You can preview results in DEBUG tab:  Just click Start emulation to see the end result.";
            msg = msg + "\r\n -- You can fine tune the images or edit: Use Edit & Fix tab.";
            msg = msg + "\r\n -- Use Preferences to hide grid, mlt marks or other helpful polluters.";
            msg = msg + "\r\n -- Use Spacebar to drag image, use zoom option to move closer to the image";*/
            MessageBox.Show(textBox9.Text , "Welcome");

        }

        private void comboBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBox6.Text = info[comboBox4.SelectedIndex];
            textBox4.Text = codes[comboBox4.SelectedIndex];
        }


        private void textMegaLZ_TextChanged(object sender, EventArgs e)
        {

        }

        private void atrributesToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void button7_Click_1(object sender, EventArgs e)
        {
            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {

                    
                        mlt[x, y] = deflash(mlt[x, y]);
                  

                }
            }
            setup();
        }

        private void chkFreeRasters_CheckedChanged(object sender, EventArgs e)
        {
            setup();
        }

        private void checkMARK_CheckedChanged(object sender, EventArgs e)
        {
            
        }

        private void trackAlpha_Scroll(object sender, EventArgs e)
        {
            UIalpha = trackAlpha.Value;
            setup();

        }

        private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {

            if (!e.Control && optShowMLTadresses) // Check if Ctrl is released and state needs to be reset
            {
                optShowMLTadresses = false;
                setup();
            }

            if (!e.Alt && optDraggingNow) // Check if Ctrl is released and state needs to be reset
            {
                optDraggingNow = false;
                pictureBox1.Cursor = Cursors.Default;

                
            }

            if (radioButton10.Checked && !e.Shift)
            {
                if (pictSpare != null)
                {
                    DisposeSwap();  // remove pict2
                }

            }
            
        }

        private void btnLoadBalance_Click(object sender, EventArgs e)
        {
            BalanceThreshold = Convert.ToInt32(txtBalance.Text);
            //12'den fazla mlt olan rasterları üst ve altta boşluk varsa oralara dağıtmaya çalışıyoruz
            
            //mlt listesi oluşturalım

            /*
            int ty = y / ch;
            int lcnt = 0;
            if (ty % 8 != 0)
                for (int k = 0; k < 32; k++)
                {
                    if (chex[k, ty] == 1)
                    {
                        lcnt++;
                        G.DrawRectangle(new Pen(Color.White), (k * cw), (y), cw, ch);

                    }
                }
            textBox10.Text = "Mlts: " + lcnt;
            if (ty % 8 == 0) textBox10.Text += " No limit at line " + ty; 
            */


            int moved = 0;
            //int[] exceeders = new int[192];

            for (int y = 0; y < 192; y++)
                for (int i = 0; i < gmax[y]; i++)
                    totmlt[y] += groups[y, i, 1];

            for (int y = 1; y < 191; y++)  //skip raster 0 nothing to copy from above
            {
                if ((totmlt[y] > BalanceThreshold) && (y%8!=0)) // do not process first raster 
                {
                    int UpR= totmlt[y-1];
                    int ThisR = totmlt[y];

                    if (UpR <= BalanceThreshold || ((y-1) % 8 == 0))
                    {
                        // we got a candidate above
                        int result=CopyFromAboveRaster(y);
                        moved += ThisR - result;
                    }

                    //CopyFromBelowRaster(y);
                    //moved += totmlt[y] - UpR;
                }
            }

            //end and update
            setup();
            textBox10.Text = "Balanced " + moved.ToString() + " rasters";
            
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveRecentFiles();
        }

        private void clearRecentsToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            recentFiles.Clear();
            UpdateRecentMenu();
        }

        private void btnSetPalette_Click(object sender, EventArgs e)
        {
            setPalette(Convert.ToInt32(txtHiVal.Text), Convert.ToInt32(txtLowVal.Text));
            setup();
        }
        private int CopyFromAboveRaster(int y, bool processSimilar = false)
        {
            if (y == 0) return totmlt[y];

            for (int k = 31; k >= 0; k--)
            {
                if ((chex[k, y] != 0) && (chex[k, y - 1] == 0))
                {
                    int[] g = getattr((byte)mlt[k, y]);     // Kaynak (alttaki)
                    int[] r = getattr((byte)mlt[k, y - 1]); // Hedef (üstteki)

                    if (r[0] != r[1]) continue; // Hedefin tek renkli olduğunu garantile

                    bool matchFound = false;
                    bool needsInvert = false;

                    // Kural 2: Siyah Renge Özel Durum (Parlaklık kontrolü yok)
                    if (IsColorBlack(r[0]))
                    {
                        if (IsColorBlack(g[0])) // Siyah INK ile eşleşme
                        {
                            matchFound = true;
                            needsInvert = false;
                        }
                        else if (IsColorBlack(g[1])) // Siyah PAPER ile eşleşme
                        {
                            matchFound = true;
                            needsInvert = true;
                        }
                    }

                    // Eğer siyah kuralı uygulanmadıysa, normal kuralları uygula
                    if (!matchFound)
                    {
                        // Kural 1 & 3: Normal ve Benzer Renk Eşleşmesi (Parlaklık kontrolü var)
                        if (g[2] == r[2]) // Parlaklık bitleri uyuşmalı
                        {
                            bool inkMatch = processSimilar ? AreColorsSimilar(r[0], g[0]) : (r[0] == g[0]);
                            bool paperMatch = processSimilar ? AreColorsSimilar(r[0], g[1]) : (r[0] == g[1]);

                            if (inkMatch)
                            {
                                matchFound = true;
                                needsInvert = false; // INK eşleşti, rolleri değiştirme
                            }
                            else if (paperMatch)
                            {
                                matchFound = true;
                                needsInvert = true;  // PAPER eşleşti, rolleri ters çevir
                            }
                        }
                    }

                    // === EYLEM KISMI ===
                    if (matchFound)
                    {
                        // 1. Attribute verisini üst hücreye kopyala.
                        mlt[k, y - 1] = mlt[k, y];

                        // 2. Bitmap verisini kopyala (gerekirse ters çevirerek).
                        bmp[k, y - 1] = needsInvert ? (byte)~bmp[k, y] : bmp[k, y];

                        // 3. Durum bayraklarını ve sayaçları güncelle.
                        chex[k, y] = 0;
                        totmlt[y]--;
                        chex[k, y - 1] = 1;
                        totmlt[y - 1]++;

                        // 4. Dengeleme eşiklerini kontrol et.
                        if ((totmlt[y - 1] > BalanceThreshold) && (y % 8 != 0)) break; //first raster has no limit
                        if (totmlt[y] <= BalanceThreshold) break;
                    }
                }
            }
            return totmlt[y];
        }

        private void CopyFromAboveRaster2(int y)
        {
            for (int k = 31; k >= 0; k--) //scan from right so raster has more time to update
            {

                if ((chex[k, y] != 0) && (chex[k, y - 1] == 0)) //if it's a multicolor cell but above a normal cell    //(chex[k, fully] != 0) && 
                {

                    int[] r = getattr((byte)mlt[k, (y-1)]); //get above cell colors
                    int[] g = getattr((byte)mlt[k, y]); //get current cell colors
                    if ((r[0] == r[1]) && ((g[0] == r[0]) || (g[1] == r[0]) || (g[0] == r[1]) || (g[1] == r[1]))) //if above cell has same colors as current cell
                    {
                        //we got a candidate!
                        //
                        mlt[k, y] = mlt[k, (y - 1)];
                        bmp[k, y] = bmp[k, (y - 1)];
                        chex[k, y] = 0; //no more multicolor
                        totmlt[y]--;
                        totmlt[y - 1]++;
                        if (totmlt[y - 1] > BalanceThreshold) break; //no more room above

                    }
                }


            }
        }


        /// <summary>
        /// Verilen iki rengin benzer olup olmadığını kontrol eder (processSimilar=true durumu için).
        /// Kırmızı <=> Mor, Yeşil <=> Cyan, Sarı <=> Beyaz çiftlerini ve birebir eşleşmeleri kontrol eder.
        /// </summary>
        private bool AreColorsSimilar(int color1, int color2)
        {
            if (color1 == color2) return true;

            // Renkleri parlaklık (brightness) bilgisinden arındırarak temel renkleri karşılaştıralım.
            int baseColor1 = color1 % 8;
            int baseColor2 = color2 % 8;

            // Kırmızı (2) <=> Mor (3)
            if ((baseColor1 == 2 && baseColor2 == 3) || (baseColor1 == 3 && baseColor2 == 2)) return true;
            // Yeşil (4) <=> Cyan (5)
            if ((baseColor1 == 4 && baseColor2 == 5) || (baseColor1 == 5 && baseColor2 == 4)) return true;
            // Sarı (6) <=> Beyaz (7)
            if ((baseColor1 == 6 && baseColor2 == 7) || (baseColor1 == 7 && baseColor2 == 6)) return true;

            return false;
        }

        /// <summary>
        /// Bir rengin siyah olup olmadığını kontrol eder (Normal Siyah: 0, Parlak Siyah: 8).
        /// </summary>
        private bool IsColorBlack(int color)
        {
            return color == 0 || color == 8;
        }


        private void CopyFromBelowRaster(int y) //auto fix utility
        {
            int freey = y;
            int fully = (y + 1);
            
            for (int k = 31; k >=0; k--) //scan from right so raster has more time to update
            {

                if (totmlt[freey] <= BalanceThreshold) //BalanceThreshold default 10
                {
                    if ((chex[k, freey] == 0)) //if not a multicolor cell     //(chex[k, fully] != 0) && 
                    {
                        if (CopyFromBelowAndFix(k, freey))
                        {
                            totmlt[freey]++;
                            totmlt[fully]--;
                        }
                    }
                }
                else
                {
                    break;
                }
            }
            
        }

        private bool CopyFromBelowAndFix(int cellX, int cellY) //editor tool
        {
            int fully = cellY + 1;
            int freey = cellY;
            int k = cellX;


                int[] freerenk = getattr((byte)mlt[k, freey]); //0=ink  1=paper  2=bri
                int[] fullrenk = getattr((byte)mlt[k, fully]); //0=ink  1=paper  2=bri
                if ((bmp[k, freey] == 0) || (bmp[k, freey] == 255)) //do we have one unused color?
                {
                    //if ((bmp[k, fully] == 0) || (bmp[k, fully] == 255)) //oh the bottom is also 
                    
                    
                    //just attrib data, check colors if brightness match and the bottom color has color of free cell.
                    if (freerenk[2] == fullrenk[2])
                    {
                        mlt[k, freey] = mlt[k, fully]; //copy color data
                        if ((bmp[k, freey] == 255))
                        {   //full ink
                            if (freerenk[0] == fullrenk[0])
                            {
                                //inks are matching just copy the colors:
                                //nothing to do really.
                            }
                            else if (freerenk[0] == fullrenk[1])
                            {
                                //reversed colors, reverse bitmap and copy
                                bmp[k, freey] = 0;
                            }

                        }
                        else if (bmp[k, freey] == 0)
                        { //full paper
                            if (freerenk[1] == fullrenk[0])
                            {
                                bmp[k, freey] = 255;

                            }
                            else if (freerenk[1] == fullrenk[1])
                            {
                                //paper already match, do nothing
                            }
                        }

                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                return false;

            }




        

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            label19.Text = "Contention model: " + comboBox2.Text;
        }

        private void button5_Click_1(object sender, EventArgs e)
        {
            regenerate();
        }

        private void button15_Click_1(object sender, EventArgs e)
        {
            startemulation();
        }

        private void button16_Click_1(object sender, EventArgs e)
        {
            exportTap();
        }

        private void button17_Click_1(object sender, EventArgs e)
        {
            button9_Click(sender, e);

        }

        private void sWAPBuffersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int[,] smlt = new int[32, 192];
            byte[,] sbmp = new byte[32, 192];
            int[,] schex = new int[32, 192]; //holds bytes to update in every interrupt. Creates rainbow effect.
        

            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {

                    smlt[x, y] = mlt[x, y];
                    sbmp[x, y] = bmp[x, y];
                    schex[x, y] = chex[x, y];
                   
                }
            }

            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {

                    mlt[x, y] = umlt[x, y];
                    bmp[x, y] = ubmp[x, y];
                    chex[x, y] = uchex[x, y];
                }
            }

            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 32; x++)
                {

                    umlt[x, y] = smlt[x, y];
                    ubmp[x, y] = sbmp[x, y];
                    uchex[x, y] = schex[x, y];
                }
            }
            setup();
        }

        private void radioButton10_CheckedChanged(object sender, EventArgs e)
        {
            groupBox4.Visible = radioButton10.Checked;
        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            groupBox4.Visible = radioButton5.Checked;
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            isHeldDown = false;
        }

        private void button18_Click_1(object sender, EventArgs e)
        {

            replaceColor(ink, paper, bright);
            setup();
          

        }


        private void replaceColor(int fromColor, int toColor, int brightness)
        {
            if (brightness > 0) brightness = 8;
            if (fromColor<8) fromColor+=brightness;
            if (toColor<8) toColor+=brightness;

              for (int y=0;y<192;y++)
                for (int x = 0; x < 32; x++)
                {
                    var rx = getattr((byte)mlt[x, y]);  //renkler brighness ile toplanmış şekilde dönüyor

                    if (rx[0] == fromColor)
                    {
                        mlt[x,y]=MakeAttrByte(toColor, rx[1], rx[2]);
                    }

                    if (rx[1] == fromColor)
                    {
                        mlt[x, y] = MakeAttrByte(rx[0], toColor , rx[2]);
                    }

                }
        }



    }
}