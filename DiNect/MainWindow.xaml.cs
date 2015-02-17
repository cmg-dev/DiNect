using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Threading;

namespace DiNect
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        KinectWindow KinCon;

        /* min and max pixel values */
        private int _max;
        private int _min;
        /* we want to store the default min/max- pixel values */
        private int _defaultMax;
        private int _defaultMin;

        private double _center;
        private double _currentLevel;

        public double center
        {
            get { return _center; }
            set { _center = (double) value;
                ///* check the borders [ _defaultMin,_defaultMax ] */
                //_center = ( value > _defaultMax ) ? _defaultMax : value;
                //_center = ( _center < _defaultMin ) ? _defaultMin : _center;
                adjustWindow( );

            }
         }

        public double currentLevel
        {
            get { return _currentLevel; }
            set {
                this._currentLevel = ( double ) value; 
                /* check the borders */
                adjustLevel( );

            }
        }

        private int[] LUT;

        int HIST_WIDTH = 256;
        int HIST_HEIGHT = 128;

        BitmapSource imgOriginal;
        BitmapSource imgAdjusted;

        byte[] p = null;

        private byte[ ] latchImgOrig( ) {
            /* latch in the image */
            BitmapSource bmp;
            Uri uri = new Uri( "D:\\06_dev\\02_kinect\\DiNect\\DiNect\\ct1.bmp", 
                                UriKind.RelativeOrAbsolute );

            BmpBitmapDecoder dec = new BmpBitmapDecoder( uri, 
                BitmapCreateOptions.PreservePixelFormat, 
                BitmapCacheOption.Default );

            bmp = dec.Frames[ 0 ];

            /* create local copy of the pixels */
            p = new byte[ ( int ) ( bmp.Height * bmp.Width ) ];
            int stride = ( ( int ) bmp.Width + 3 ) & ~3;
            bmp.CopyPixels( p, stride, 0 );

            imgOriginal = BitmapSource.Create(
                ( int ) bmp.Width,
                ( int ) bmp.Height,
                96,
                96,
                PixelFormats.Gray8,
                getAdjustedBitmapPalette( ),
                p,
                stride );

            imgOriginal.Freeze( );

            this.lblImgWidth.Content = bmp.Width.ToString( );
            this.lblImgHeight.Content = bmp.Height.ToString( );
            /* return the copy */
            return p;

        }

        public MainWindow()
        {
            /* init the histogram */
            int width = HIST_WIDTH;
            int height = HIST_HEIGHT;

            InitializeComponent();
            //KinCon = new KinectWindow( this );

            //KinCon.LevelChanged += handle_LevelChanged;
            //KinCon.WindowChanged += handle_WindowChanged;

            //KinCon.Show( );
            initLUT( );

            /* read in the original Image */
            byte[] pixelsA = latchImgOrig( );

            imgOriginal.Freeze( );
            this.imgOrig.Source = imgOriginal;

            getMinMaxCenter( );

            this.lblCenter.Content = _center.ToString( );
            this.lblMax.Content = _max.ToString( );
            this.lblMin.Content = _min.ToString( );

            updateLUT( );

            int stride = ( ( int ) imgOriginal.Width + 3 ) & ~3;
            imgAdjusted = BitmapSource.Create(
                ( int ) imgOriginal.Width,
                ( int ) imgOriginal.Height,
                96,
                96,
                PixelFormats.Gray8,
                getAdjustedBitmapPalette( ),
                p,
                stride );

            imgAdjusted.Freeze( );

            this.imgAdjust.Source = imgAdjusted;
            this.imgAdjust.Source.Freeze( );

            drawHistogram( );
            
            updateImg.WorkerReportsProgress = true;
            updateImg.WorkerSupportsCancellation = true;

            // DoWorkEventHandler ************************************************************
            updateImg.DoWork += updateImgAdjusted_Work;
            // RunWorkerCompletedEventHandler ************************************************
            updateImg.RunWorkerCompleted += updateImgAdjusted_Finished;

            updateImg.RunWorkerAsync( );
        }


        private void drawHistogram( int[ ] hist, long sum ) {
            /* we need the right scale for this... */
            int width = HIST_WIDTH;
            int height = HIST_HEIGHT;

            int histWidth = 0;
            double scale;
            bool isLogScale = false;

            byte[] p = new byte[ HIST_WIDTH * HIST_HEIGHT ];
            int stride = ( ( int ) imgOriginal.Width + 3 ) & ~3;

            if( hist != null ) {
                histWidth = hist.Length;
                int[] tmp = ( int[ ] ) hist.Clone( );

                Array.Sort( tmp );

                /* determine the scale for the Image */
                int largest = tmp[ hist.Length - 1 ];
                scale = ( double ) HIST_HEIGHT / ( double ) largest;
                if( largest > ( sum * 0.3 ) ) {
                    /* if it is to small use log-scaling */
                    isLogScale = true;
                    scale = Math.Log( tmp[ hist.Length - 1 ] );

                }
                
                int y = 0;
                PointCollection points = new PointCollection( );
                points.Add( new Point( 0, HIST_HEIGHT ) );
                for( int i = 0; i < histWidth; i++ ) {
                    if( isLogScale )
                        y = hist[ i ] == 0 ? 0 : ( int ) ( HIST_HEIGHT * Math.Log( hist[ i ] ) / scale );
                    else
                        y = ( int ) ( hist[ i ] * scale );

                    if( y > HIST_HEIGHT )
                        y = HIST_HEIGHT;

                    points.Add( new Point( i+1, HIST_HEIGHT - y ) );

                }
                points.Add( new Point( 256, HIST_HEIGHT ) );
                points.Freeze( );
                this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<PointCollection>( SetPolyPoints ), points );

                /* adjust the slope graph */
                drawSlope( );
            }
        }

        private void drawHistogram( ) {
            long sum = 0;
            int[] h = calcHistogram( imgOriginal, out sum );
            drawHistogram( h, sum );

        }

        private void updateHistogram( ) {
            drawHistogram( null, 0 );

        }

        private void drawSlope( ) {
            double x1, x2, y1, y2;
            x1 = x2 = y1 = y2 = 0.0;
            double deltaX = 0.0;
            double deltaY = 0.0;
            double m = 0.0;
            double b = 0.0;

            if( _min < 0 || _max > _defaultMax ) {
                deltaX = _max - _min;
                deltaY = HIST_HEIGHT;

                /* berechne mx+b im ursprung */
                m = deltaY / deltaX;
                b = m * _max;

                x1 = _min < 0 ? 0: _min;
                x2 = _max > HIST_WIDTH ? HIST_WIDTH : _max;

                if( x1 > _defaultMin )
                    y1 = HIST_HEIGHT;
                else 
                    y1 = b;
                if( x2 < HIST_WIDTH )
                    y2 = 0;
                else
                    y2 = ( HIST_HEIGHT - ( int ) b ) < 0 ? ( ( int ) b - HIST_HEIGHT ) : ( HIST_HEIGHT - ( int ) b );

            } else { 
                x1 = _min;
                x2 = _max;
                y1 = HIST_HEIGHT;
                y2 = 0.0;

            }
            PointCollection points = new PointCollection( );
            points.Add( new Point( x1, y1 ) );
            points.Add( new Point( x2, y2 ) );
            points.Freeze( );
            this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<PointCollection>( SetSlopePoints ), points );
            //Slope.X1 = x1;
            //Slope.X2 = x2;
            //Slope.Y1 = y1;
            //Slope.Y2 = y2;

            int center;
            //if( _min > _defaultMin && _defaultMax )
            center = _min + ( _max - _min ) / 2;
            //Center.X1 = center;
            //Center.X2 = center;
            //Center.Y1 = HIST_HEIGHT;
            //Center.Y2 = 0;

            PointCollection points1 = new PointCollection( );
            points1.Add( new Point( center, HIST_HEIGHT ) );
            points1.Add( new Point( center, HIST_HEIGHT / 2 ) );
            points1.Freeze( );
            this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<PointCollection>( SetCenterPoints ), points1 );

            this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<String>( SetMinMaxCenter ), "test" );

        }
        
        /* create and initialize the LUT */
        private void initLUT( ) {
            /* init the LUT-length */
            //this.LUT = new int[ 3,this._max ];
            this._max = 256;
            this.LUT = new int[ this._max ];

            /* init the LUT */
            for( int i = 0; i < this.LUT.Length; i++ ) {
                this.LUT[ i ] = i;

            }
        }

        /* we update the LUT so we can use ist for manipulating the Pixel in the Adjusted-Image */
        private void updateLUT( ) {
            int range = _max - _min;
            double slope = ( double ) _defaultMax / ( double ) range;

            /* init the LUT */
            for( int i = 0; i < this.LUT.Length; i++ ) {
                if( i < _min )
                    this.LUT[ i ] = 0;
                else if( i > _max )
                    this.LUT[ i ] = this.LUT.Length - 1;
                else
                    this.LUT[ i ] = ( int ) ( ( double ) ( i - _min ) * slope );

            }
        }

        /*  */
        private void getMinMaxCenter( ) {
            string Format = "";

            _defaultMax = _defaultMin = _min = _max = 0;

            switch( Format ) {
                case "Format32bppArgb":
                    //_min = 0; _max = (int)4294967298;
                    break;
                case "Format8bppIndexed":
                    _min = 0;
                    _max = 255;
                    break;
                case "weitere Formate hier":
                    break;

                default:
                    /* per default we use grayscale */
                    _min = 0;
                    _max = 255;
                    break;
            }
            _currentLevel = _center = ( double ) ( _max - _min ) / 2;
            _defaultMax = _max;
            _defaultMin = _min;

            if( _center == 0 ) {
                Console.WriteLine( "DiNect::Warning: Center could not be calculated, may pixelformat is unknown" );

            } else if( _center < 0 ) {
                Console.WriteLine( "DiNect::Error: Center could not be calculated, may pixelformat is unknown; Center is: " + _center );
                _center = 0;

            }
        }

        BackgroundWorker updateImg = new BackgroundWorker( );

        private void updateImgAdjusted_Work( object sender, DoWorkEventArgs e )
        {
            int stride = ( ( int ) imgAdjusted.Width + 3 ) & ~3;
            int size = ( int ) imgAdjusted.Width * ( int ) imgAdjusted.Height;

            byte[] _p = new byte[ p.Length ];

            BitmapPalette myPalette = getAdjustedBitmapPalette( );

            for( int i = 0; i < _p.Length; i++ )
                _p[ i ] = myPalette.Colors[ ( int ) p[ i ] ].B;

            //imgAdjust.BeginInit( );
            ImageSource imgS = BitmapSource.Create(
                ( int ) imgAdjusted.Width,
                ( int ) imgAdjusted.Height,
                96,
                96,
                PixelFormats.Gray8,
                myPalette,
                _p,
                stride );
            
            imgAdjusted.Freeze( );
            if( imgS.IsFrozen == false )
                imgS.Freeze( );

            e.Result = imgS;

        }

        private void updateImgAdjusted_Finished( object sender, RunWorkerCompletedEventArgs e )
        {
            if( e.Result != null ) {
                this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<ImageSource>( SetImage ), ( ImageSource ) e.Result );
                //this.imgAdjust.Source = ( ImageSource ) e.Result;

            }
        }

        public void SetImage( ImageSource source ) {
            this.imgAdjust.Source = source;
        }

        public void SetPolyPoints( PointCollection points ) {
            this.Poly.Points = points;
        }

        public void SetMinMaxCenter( String s ) {
            this.lblCenter.Content = _center.ToString( );
            this.lblMax.Content = _max.ToString( );
            this.lblMin.Content = _min.ToString( );

        }

        public void SetSlopePoints( PointCollection points ) {
            this.Slope.X1 = points[ 0 ].X;
            this.Slope.Y1 = points[ 0 ].Y;
            this.Slope.X2 = points[ 1 ].X;
            this.Slope.Y2 = points[ 1 ].Y;

        }

        public void SetCenterPoints( PointCollection points ) {
            this.Center.X1 = points[ 0 ].X;
            this.Center.Y1 = points[ 0 ].Y;
            this.Center.X2 = points[ 1 ].X;
            this.Center.Y2 = points[ 1 ].Y;

        }


        private void updateImgAdjusted( ) {
            int stride = ( ( int ) imgAdjusted.Width + 3 ) & ~3;
            int size = ( int ) imgAdjusted.Width * ( int ) imgAdjusted.Height;

            byte[] _p = new byte[ p.Length ];

            BitmapPalette myPalette = getAdjustedBitmapPalette( );
            
            for(int i = 0; i < _p.Length; i++ )
                _p[i] = myPalette.Colors[ (int)p[ i ] ].B;

            ImageSource imgS = BitmapSource.Create(
                ( int ) imgAdjusted.Width,
                ( int ) imgAdjusted.Height,
                96,
                96,
                PixelFormats.Gray8,
                myPalette,
                _p,
                stride );

            imgAdjusted.Freeze( );
            if( imgS.IsFrozen == false ) 
                imgS.Freeze( );

            this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<ImageSource>( SetImage ), imgS );

        }

        private delegate void OneArgDelegate( String arg );

        double bvalue = 127.5;
        public void adjustWindow( ) {
            double center = ( _defaultMax - _defaultMin ) * ( ( _defaultMax - bvalue ) / _defaultMax );
            double width = _max - _min;
            _min = ( int ) ( _center - ( width / 2.0 ) );
            _max = ( int ) ( _center + ( width / 2.0 ) );

            updateLUT( );
            drawHistogram( );
            updateImgAdjusted( );

        }

        /* 
         * This function is able to adjust the level of the displayed image 
         */
        public void adjustLevel( ) {
            /* steigung */
            double slope;
            double range;
            range = ( _defaultMax ) - _defaultMin;
            double mid = 127.5;

            if( _currentLevel <= mid )
                slope = ( _currentLevel ) / mid;
            else
                slope = mid / ( (_defaultMax+1 ) - _currentLevel );

            slope = Math.Round( slope, 3 );

            if( slope > 0.0 ) {
                _min = ( int ) ( _center - ( 0.5 * range ) / slope );
                _max = ( int ) ( _center + ( 0.5 * range ) / slope );
            }

            updateLUT( );
            drawHistogram( );
            updateImgAdjusted( );

        }

        /* This will determine the ColorPalette according to the LUT we adjust */
        private BitmapPalette getAdjustedBitmapPalette( ) {
            List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>( );
            byte val = 0;
            for( int i = 0; i < 256; i++ ) {
                val = ( byte ) LUT[ i ];
                colors.Add( Color.FromArgb( 255, val, val, val ) );
            }
            BitmapPalette myPalette = new BitmapPalette( colors );

            return myPalette;
        }

        /** 
         * Calculates the Histogram of a Bitmap-Image 
         * @param[in] bmp Bitmap we need the Hist from
         * @return int[] the Histogram 
         *
         */
        private int[ ] calcHistogram( BitmapSource bmp, out long sum ) {
            byte[] p = new byte[ ( int ) ( bmp.Height * bmp.Width ) ];
            int stride = ( ( int ) bmp.Width + 3 ) & ~3;
            bmp.CopyPixels( p, stride, 0 );

            sum = 0;

            int[] hist = new int[ 256 ];

            for( int i = 0; i < p.Length-1; i++ ) {
                if( p[i] == 255 )
                    continue;
                hist[ p[i] ]++;
                sum++;

            }
            return hist;

        }
        
        private void Window_KeyDown( object sender, KeyEventArgs e ) {
            /* check for esc key */
            if( e.Key.ToString() == "Escape" ) {
                Close( );
            }
            /* level down */
            if( e.Key.ToString( ) == "A" ) {
                _currentLevel -= 1;
                adjustLevel( );
            }
            /* level up */
            if( e.Key.ToString( ) == "D" ) {
                _currentLevel += 1;
                adjustLevel( );
            }
            /* window up */
            if( e.Key.ToString( ) == "W" ) {
                _center += 1;
                //bvalue += 1;
                adjustWindow( );
            }
            /* window down */
            if( e.Key.ToString( )== "S" ) {
                _center -= 1;
                //bvalue -= 1;
                adjustWindow( );
            }
        }

        /** 
         * 
         */
        void handle_LevelChanged( object sender, LevelChangedEventArgs args ) {
            switch( ( int ) args.mode ) {
                case ( int ) Mode.UP:
                    _currentLevel -= 5;
                    break;
                case ( int ) Mode.DOWN:
                    _currentLevel += 5;
                    break;
                case ( int ) Mode.UP_SLOW:
                    _currentLevel -= 1;
                    break;
                case ( int ) Mode.DOWN_SLOW:
                    _currentLevel += 1;
                    break;
                case ( int ) Mode.UP_FAST:
                    _currentLevel -= 5;
                    break;
                case ( int ) Mode.DOWN_FAST:
                    _currentLevel += 5;
                    break;
                case (int) Mode.RESET:
                    _currentLevel = 127;
                    break;
                default:
                    return;

            }

            adjustLevel( );

        }

        /** 
         * 
         */
        void handle_WindowChanged( object sender, WindowChangedEventArgs args ) {
            switch( ( int ) args.mode ) {
                case ( int ) Mode.UP:
                    center += 5;
                    break;
                case ( int ) Mode.DOWN:
                    center -= 5;
                    break;
                case ( int ) Mode.UP_SLOW:
                    center += 1;
                    break;
                case ( int ) Mode.DOWN_SLOW:
                    center -= 1;
                    break;
                case ( int ) Mode.UP_FAST:
                    center += 5;
                    break;
                case ( int ) Mode.DOWN_FAST:
                    center -= 5;
                    break;
                case ( int ) Mode.RESET:
                    center = 127;
                    break;
                default:
                    return;

            }
            adjustWindow( );

        }
    }
}
