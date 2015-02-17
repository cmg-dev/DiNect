using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Threading;
using OpenNI;
using System.Drawing;
using System.Windows.Threading;

namespace DiNect {
    /// <summary>
    /// Interaktionslogik für KinectWindow.xaml
    /// </summary>
    public partial class KinectWindow : Window {
        BackgroundWorker imgFetcher = new BackgroundWorker( );
        BackgroundWorker skeletonDrawer = new BackgroundWorker( );

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged( String info ) {
            if( PropertyChanged != null ) {
                //The following causes a "the calling thread cannot access this object because a different thread owns it" error!
                PropertyChanged( this, new PropertyChangedEventArgs( info ) );
            }
        }

        void imgFetcher_DoWorkCompleted( object sender, RunWorkerCompletedEventArgs e ){
            if (e.Result != null) {
                KinectCamImg.Source = (ImageSource)e.Result;
                    ////Console.WriteLine( e.Result.ToString( ) );
                int[] users = this.userGenerator.GetUsers( );
                /* draw strings for each user */
                foreach( int user in users ) {
                    string label = "";
                    if( !this.shouldPrintState )
                        label += user;
                    else if( this.skeletonCapbility.IsTracking( user ) ) {
                        label += user + " - Tracking";
                        Griddi.Background = System.Windows.Media.Brushes.Lime;

                    } else if( this.skeletonCapbility.IsCalibrating( user ) ) {
                        label += user + " - Calibrating...";
                         Griddi.Background = System.Windows.Media.Brushes.Yellow;
                    } else
                        label += user + " - Looking for pose";

                    label1.Content = label;

                    if( this.skeletonCapbility.IsTracking( user ) == true && _drawSkeleton )
                        DrawSkeleton( anticolors[ user % ncolors ], user );
                    
                }   
            }
            /* reinvoke the imgFetcher */
            imgFetcher.RunWorkerAsync( );

        }
        
        /** 
         *  The Backgroundworker will work here...
         * 
         */
        unsafe void imgFetcher_FetchImage(object sender, DoWorkEventArgs e) {
            DepthMetaData depthMD = new DepthMetaData( );
            while( true ) {
                try {
                    this.context.WaitOneUpdateAll( this.depth );
                    break;

                } catch( Exception ex ) {
                    Console.WriteLine( "There was an Exeption '" + ex.Message + "'" );

                }
            }
            /* get the raw data */
            this.depth.GetMetaData( depthMD );
            /* calculates the depth histogram */
            CalcHist( depthMD );

            lock( this ) {
                int stride = ( int ) depthMD.XRes * 3 + ( ( int ) depthMD.XRes % 4 );
                byte [] p = new byte[ depthMD.XRes * stride ];

                if( this.shouldDrawPixels ) {
                    ushort* pDepth = ( ushort* ) this.depth.DepthMapPtr.ToPointer( );
                    ushort* pLabels = ( ushort* ) this.userGenerator.GetUserPixels( 0 ).LabelMapPtr.ToPointer( );

                    // set pixels
                    for( int y = 0; y < depthMD.YRes; ++y ) {
                        /* get the Pointer */
                        fixed( byte* pP = p ) {
                            byte * pDest = pP + y * stride;
                            for( int x = 0; x < depthMD.XRes; ++x, ++pDepth, ++pLabels , pDest += 3 ) {
                                ushort label = *pLabels;
                                if( this.shouldDrawBackground || *pLabels != 0 ) {
                                    System.Drawing.Color labelColor = System.Drawing.Color.White;
                                    if( label != 0 ) {
                                        labelColor = colors[ 0 ];
                                    }

                                    byte pixel = ( byte ) this.histogram[ *pDepth ];
                                    pDest[ 0 ] = ( byte ) ( pixel * ( labelColor.B / 256.0 ) );
                                    pDest[ 1 ] = ( byte ) ( pixel * ( labelColor.G / 256.0 ) );
                                    pDest[ 2 ] = ( byte ) ( pixel * ( labelColor.R / 256.0 ) );
                                }
                            }
                        }
                    }
                }
                ImageSource imgKinect = BitmapSource.Create(
                        ( int ) depthMD.XRes,
                        ( int ) depthMD.YRes,
                        96,
                        96,
                        PixelFormats.Rgb24,
                        BitmapPalettes.WebPalette,
                        p,
                        stride );

                imgKinect.Freeze( );

                e.Result = imgKinect;

            }
        }

        public KinectWindow( MainWindow mainWindow ) {
            InitializeComponent( );

            imgFetcher.WorkerReportsProgress = true;
            imgFetcher.WorkerSupportsCancellation = true;

            // DoWorkEventHandler ************************************************************
            imgFetcher.DoWork += imgFetcher_FetchImage;
            // RunWorkerCompletedEventHandler ************************************************
            imgFetcher.RunWorkerCompleted += imgFetcher_DoWorkCompleted;

            /*****************************************************************************/
            this.context = Context.CreateFromXmlFile( SAMPLE_XML_FILE, out scriptNode );
            this.depth = context.FindExistingNode( NodeType.Depth ) as DepthGenerator;
            if( this.depth == null ) {
                throw new Exception( "Viewer must have a depth node!" );

            }

            this.userGenerator = new UserGenerator( this.context );
            this.skeletonCapbility = this.userGenerator.SkeletonCapability;
            this.poseDetectionCapability = this.userGenerator.PoseDetectionCapability;
            this.calibPose = this.skeletonCapbility.CalibrationPose;

            this.userGenerator.NewUser += userGenerator_NewUser;
            this.userGenerator.LostUser += userGenerator_LostUser;
            this.poseDetectionCapability.PoseDetected += poseDetectionCapability_PoseDetected;
            this.skeletonCapbility.CalibrationComplete += skeletonCapbility_CalibrationComplete;

            this.skeletonCapbility.SetSkeletonProfile( SkeletonProfile.All );
            this.joints = new Dictionary<int, Dictionary<SkeletonJoint, SkeletonJointPosition>>( );
            this.userGenerator.StartGenerating( );

            /* init the gesture generator */
            this.gesture = this.context.FindExistingNode( OpenNI.NodeType.Gesture ) as OpenNI.GestureGenerator;
            gesture.AddGesture( "Wave" );
            gesture.AddGesture( "Click" );

            //gesture.EnumerateAllGestures( );
            gesture.GestureRecognized += gesture_GestureRecognized;
            gesture.GestureProgress += gesture_GestureIsRecording;
            gesture.StartGenerating( );

            /***************************************/

            /* init the hands generator */
            this.hands = this.context.FindExistingNode( OpenNI.NodeType.Hands ) as OpenNI.HandsGenerator;
            hands.HandUpdate += hands_HandsUpdated;

            /***************************************/

            this.histogram = new int[ this.depth.DeviceMaxDepth ];

            MapOutputMode mapMode = this.depth.MapOutputMode;

            //this.bitmap = new Bitmap( ( int ) mapMode.XRes, ( int ) mapMode.YRes/*, System.Drawing.Imaging.PixelFormat.Format24bppRgb*/);


            imgFetcher.RunWorkerAsync( );

            Console.WriteLine( Canvas.GetLeft( FOV ) );
            Console.WriteLine( Canvas.GetRight( FOV ) );
            Console.WriteLine( Canvas.GetTop( FOV ) );
            Console.WriteLine( Canvas.GetBottom( FOV ) );

            this.p_TL.X = Canvas.GetLeft( FOV );
            this.p_TL.Y = Canvas.GetTop( FOV );

           
            this.p_BR.X = this.p_TL.X + this.FOV.Width;
            this.p_BR.Y = this.p_TL.Y + this.FOV.Height;
            this.mainWindow = mainWindow;

            /*****************************************************************************/
        }

        /**
         * This will be invoked when the calibration is finished 
         * We can start tracking in this block.
         * 
         */
        void skeletonCapbility_CalibrationComplete( object sender, CalibrationProgressEventArgs e ) {
            if( e.Status == CalibrationStatus.OK ) {
                this.skeletonCapbility.StartTracking( e.ID );
                this.joints.Add( e.ID, new Dictionary<SkeletonJoint, SkeletonJointPosition>( ) );
            } else if( e.Status != CalibrationStatus.ManualAbort ) {
                if( this.skeletonCapbility.DoesNeedPoseForCalibration ) {
                    this.poseDetectionCapability.StartPoseDetection( calibPose, e.ID );
                } else {
                    this.skeletonCapbility.RequestCalibration( e.ID, true );
                }
            }
        }

        /* Appears when a pose has been detected */
        void poseDetectionCapability_PoseDetected( object sender, PoseDetectedEventArgs e ) {
            this.poseDetectionCapability.StopPoseDetection( e.ID );
            this.skeletonCapbility.RequestCalibration( e.ID, true );

        }

        /**
         * This Event will be raised if a user appears in the scene
         * This block requests the calibration from the Hardware and will raise "calibration finished Event"
         *
         */
        void userGenerator_NewUser( object sender, NewUserEventArgs e ) {
            if( this.skeletonCapbility.DoesNeedPoseForCalibration ) {
                this.poseDetectionCapability.StartPoseDetection( this.calibPose, e.ID );
            } else {
                this.skeletonCapbility.RequestCalibration( e.ID, true );
            }
        }

        /*
         * We get the Event if a user gets lost from the scene
         *
         */
        void userGenerator_LostUser( object sender, UserLostEventArgs e ) {
            this.joints.Remove( e.ID );
        }

        /*
         * This function calculates the depth-histogram
         * We use this to colorize the scene and displaying the depth-record
         *
         */
        private unsafe void CalcHist( DepthMetaData depthMD ) {
            // reset
            for( int i = 0; i < this.histogram.Length; ++i )
                this.histogram[ i ] = 0;

            ushort* pDepth = ( ushort* ) depthMD.DepthMapPtr.ToPointer( );
            int points = 0;
            for( int y = 0; y < depthMD.YRes; ++y ) {
                for( int x = 0; x < depthMD.XRes; ++x, ++pDepth ) {
                    ushort depthVal = *pDepth;
                    if( depthVal != 0 ) {
                        this.histogram[ depthVal ]++;
                        points++;
                    }
                }
            }

            for( int i = 1; i < this.histogram.Length; i++ ) {
                this.histogram[ i ] += this.histogram[ i - 1 ];
            }

            if( points > 0 ) {
                for( int i = 1; i < this.histogram.Length; i++ ) {
                    this.histogram[ i ] = ( int ) ( 256 * ( 1.0f - ( this.histogram[ i ] / ( float ) points ) ) );
                }
            }
        }

        private void GetJoint( int user, SkeletonJoint joint ) {
            SkeletonJointPosition pos = this.skeletonCapbility.GetSkeletonJointPosition( user, joint );
            if( pos.Position.Z == 0 ) {
                pos.Confidence = 0;
            } else {
                pos.Position = this.depth.ConvertRealWorldToProjective( pos.Position );
            }
            this.joints[ user ][ joint ] = pos;
        }

        /** 
         * This method will provide us with the body nodes, we request all joints seperatly
         * 
         */
        private void GetJoints( int user ) {
            try {
                GetJoint( user, SkeletonJoint.Head );
                GetJoint( user, SkeletonJoint.Neck );

                GetJoint( user, SkeletonJoint.LeftShoulder );
                GetJoint( user, SkeletonJoint.LeftElbow );
                GetJoint( user, SkeletonJoint.LeftHand );

                GetJoint( user, SkeletonJoint.RightShoulder );
                GetJoint( user, SkeletonJoint.RightElbow );
                GetJoint( user, SkeletonJoint.RightHand );

                GetJoint( user, SkeletonJoint.Torso );

                GetJoint( user, SkeletonJoint.LeftHip );
                GetJoint( user, SkeletonJoint.LeftKnee );
                GetJoint( user, SkeletonJoint.LeftFoot );

                GetJoint( user, SkeletonJoint.RightHip );
                GetJoint( user, SkeletonJoint.RightKnee );
                GetJoint( user, SkeletonJoint.RightFoot );
            
            } catch( KeyNotFoundException knfEx ) {
                throw new KeyNotFoundException( );
            
            
            }
        }

        /* in this list we store the users-skeleton */
        List< UserSkeleton > USList;

        /** 
         * Initializes the UserSkeleton-List
         * @param[ in ] user UID
         *
         */
        void initUserSkeleton( int user ) {
            if( USList == null ) 
                USList = new List<UserSkeleton>();

            if( user > 0 ) {
                UserSkeleton newUserSkeleton = new UserSkeleton( user );
                if( USList.Count == 0 ) {
                    USList.Add( newUserSkeleton );

                } else {
                    //Console.WriteLine( "initUserSkeleton:: adding user: " + user );
                    foreach( UserSkeleton us in USList ) {
                        /* check if the user is already in the list */
                        if( us.ID == newUserSkeleton.ID )
                            return;
                           
                    }
                    /* add the new user */
                    USList.Add( newUserSkeleton );

                }
            }
            return;
        }

        /* drawline-trivia */
        private Line DrawLine( System.Drawing.Color color, Dictionary<SkeletonJoint, SkeletonJointPosition> dict, SkeletonJoint j1, SkeletonJoint j2 ) {
             Point3D pos1;
             Point3D pos2;
            try {
                pos1 = dict[ j1 ].Position;
                pos2 = dict[ j2 ].Position;
            
            } catch( KeyNotFoundException knfEx ) {
                Console.Error.WriteLine( "Exeption in Drawline(): " + knfEx.Message );
                throw new KeyNotFoundException();

            }
            Line l = new Line( );
            l.X1 = ( int ) pos1.X;
            l.Y1 = ( int ) pos1.Y;
            l.X2 = ( int ) pos2.X;
            l.Y2 = ( int ) pos2.Y;
            l.Stroke = System.Windows.Media.Brushes.Orange;
            
            l.HorizontalAlignment = HorizontalAlignment.Left;
            l.VerticalAlignment = VerticalAlignment.Center;
            l.StrokeThickness = 1;
           
            //if( dict[ j1 ].Confidence == 0 || dict[ j2 ].Confidence == 0 )
                //return null;

            return l;

        }

        enum Bones {
            HEAD = 0,
            LEFTSHOULDER,
            RIGHTSHOULDER,
            NECK2LEFTARM,
            LEFTARM,
            LEFTFOREARM,
            NECK2RIGHTSHOULDER,
            RIGHTARM,
            RIGHTFOREARM,
            LEFTHIP,
            RIGHTHIP,
            BUTT,
            LEFTTHIGH,
            LEFTCALF,
            RIGHTTHIGH,
            RIGHTCALF


        };

        bool _drawSkeleton = true;

        /** 
         * Here we paint the Skeleton 
         * 
         */
        private void DrawSkeleton( System.Drawing.Color color, int user ) {
            if( user == 1 ) {
                GetJoints( user );
                Dictionary<SkeletonJoint, SkeletonJointPosition> dict = this.joints[ user ];

                if( dict.Count > 0 ) {
                    initUserSkeleton( user );
                    try {
                        UserSkeleton us;
                        for( int j = 0; j < USList.Count; j++ ) {
                            us = USList[ j ];
                            if( us.ID == user ) {
                                int i = 0;
                                foreach( Line bone in us.bones ) {
                                    switch( i++ ) {
                                        case ( int ) Bones.HEAD:
                                            us.bones[ ( int ) Bones.HEAD ] =
                                                DrawLine( color, dict, SkeletonJoint.Head, SkeletonJoint.Neck );

                                            break;

                                        case ( int ) Bones.LEFTSHOULDER:
                                            us.bones[ ( int ) Bones.LEFTSHOULDER ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftShoulder, SkeletonJoint.Torso );

                                            break;

                                        case ( int ) Bones.RIGHTSHOULDER:
                                            us.bones[ ( int ) Bones.RIGHTSHOULDER ] =
                                                DrawLine( color, dict, SkeletonJoint.RightShoulder, SkeletonJoint.Torso );

                                            break;

                                        case ( int ) Bones.NECK2LEFTARM:
                                            us.bones[ ( int ) Bones.NECK2LEFTARM ] =
                                                DrawLine( color, dict, SkeletonJoint.Neck, SkeletonJoint.LeftShoulder );

                                            break;

                                        case ( int ) Bones.LEFTARM:
                                            us.bones[ ( int ) Bones.LEFTARM ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftShoulder, SkeletonJoint.LeftElbow );

                                            break;

                                        case ( int ) Bones.LEFTFOREARM:
                                            us.bones[ ( int ) Bones.LEFTFOREARM ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftElbow, SkeletonJoint.LeftHand );

                                            break;

                                        case ( int ) Bones.NECK2RIGHTSHOULDER:
                                            us.bones[ ( int ) Bones.NECK2RIGHTSHOULDER ] =
                                                DrawLine( color, dict, SkeletonJoint.Neck, SkeletonJoint.RightShoulder );

                                            break;

                                        case ( int ) Bones.RIGHTARM:
                                            us.bones[ ( int ) Bones.RIGHTARM ] =
                                                DrawLine( color, dict, SkeletonJoint.RightShoulder, SkeletonJoint.RightElbow );

                                            break;

                                        case ( int ) Bones.RIGHTFOREARM:
                                            us.bones[ ( int ) Bones.RIGHTFOREARM ] =
                                                DrawLine( color, dict, SkeletonJoint.RightElbow, SkeletonJoint.RightHand );

                                            break;

                                        case ( int ) Bones.LEFTHIP:
                                            us.bones[ ( int ) Bones.LEFTHIP ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftHip, SkeletonJoint.Torso );

                                            break;

                                        case ( int ) Bones.RIGHTHIP:
                                            us.bones[ ( int ) Bones.RIGHTHIP ] =
                                                DrawLine( color, dict, SkeletonJoint.RightHip, SkeletonJoint.Torso );

                                            break;

                                        case ( int ) Bones.BUTT:
                                            us.bones[ ( int ) Bones.BUTT ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftHip, SkeletonJoint.RightHip );

                                            break;

                                        case ( int ) Bones.LEFTTHIGH:
                                            us.bones[ ( int ) Bones.LEFTTHIGH ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftHip, SkeletonJoint.LeftKnee );

                                            break;

                                        case ( int ) Bones.LEFTCALF:
                                            us.bones[ ( int ) Bones.LEFTCALF ] =
                                                DrawLine( color, dict, SkeletonJoint.LeftKnee, SkeletonJoint.LeftFoot );

                                            break;

                                        case ( int ) Bones.RIGHTTHIGH:
                                            us.bones[ ( int ) Bones.RIGHTTHIGH ] =
                                                DrawLine( color, dict, SkeletonJoint.RightHip, SkeletonJoint.RightKnee );

                                            break;

                                        case ( int ) Bones.RIGHTCALF:
                                            us.bones[ ( int ) Bones.RIGHTCALF ] =
                                                DrawLine( color, dict, SkeletonJoint.RightKnee, SkeletonJoint.RightFoot );

                                            break;
                                    } /* switch - end */
                                } /* foreach - end */
                                us = updateSkeleton( us );
                                USList[ j ] = us;

                            }
                        }
                    } catch( KeyNotFoundException knfEx ) {
                        Console.Error.WriteLine( "Key not found " + knfEx.Message );

                    }
                }
            }
        }

        /* here we will draw the user skeleton */
        private UserSkeleton updateSkeleton( UserSkeleton us ) {
            if( us.isDisplayed == false ) {
                us.isDisplayed = true;
                foreach( Line l in us.bones )
                    if( l != null )
                        this.KinectCanvas.Children.Add( l );

            } else {
                foreach( Line l in us.bones ) {
                    if( l != null ) {
                        for( int i = 0; i < this.KinectCanvas.Children.Count; i++ ) {
                            if( this.KinectCanvas.Children[ i ].GetType( ).Equals( l.GetType( ) ) ) {
                                this.KinectCanvas.Children.Remove( this.KinectCanvas.Children[ i ] );

                            }
                        }
                    }
                }
                foreach( Line l in us.bones ) {
                    if( l != null )
                        this.KinectCanvas.Children.Add( l );
                
                }
            }
            return us;
        }

        void SetImage( ImageSource img ) {
            this.KinectCamImg.Source = img;

        }

        /* later we will mark the tracked hand here */
        private void MarkTrackedHand( Graphics g, System.Drawing.Color color, int user ) {
            GetJoints( user );
            Dictionary<SkeletonJoint, SkeletonJointPosition> dict = this.joints[ user ];

            MarkHand( g, color, dict, SkeletonJoint.LeftHand );
            MarkHand( g, color, dict, SkeletonJoint.RightHand );

        }

        /* marks the hand-trivia */
        private void MarkHand( Graphics g, System.Drawing.Color color, Dictionary<SkeletonJoint, SkeletonJointPosition> dict, SkeletonJoint j ) {
            Point3D pos1 = dict[ j ].Position;
            int circDiameter = 20;
            int circRad = circDiameter / 2;

            if( dict[ j ].Confidence == 0 )
                return;

            g.DrawEllipse( new System.Drawing.Pen( color ), new System.Drawing.Rectangle( ( int ) pos1.X - circRad,
               ( int ) pos1.Y - circRad,
                circDiameter,
                circDiameter ) );

        }

        void setFOVOpacity( double opacity ) {
            Console.WriteLine( "Setting Opacity to: " + opacity );
            this.FOV.Opacity = opacity;

        }
        
        /*****************************************************************************************/
        /* Here we find the gesture recognition */
        int UID = 0;
        bool isHandTracking = false;
        void gesture_GestureRecognized( object sender, GestureRecognizedEventArgs e ) {
            Console.WriteLine( e.Gesture + " " + e.EndPosition.X + " " + e.EndPosition.Y + " " + e.EndPosition.Z );
            Point3D p = new Point3D( e.EndPosition.X,
                e.EndPosition.Y,
                e.EndPosition.Z );

            if( e.Gesture.ToUpper( ).Equals( "WAVE" ) ) {
                this.trackHands = !this.trackHands;
                _drawSkeleton = true;

                if( isHandTracking == false ) {
                    isHandTracking = !isHandTracking;
                    hands.StartTracking( p );
                    if( adjustMode == 0 )
                        adjustMode++;

                } else {
                    hands.StopTrackingAll( );
                    isHandTracking = !isHandTracking;

                }
            } else if( e.Gesture.ToUpper( ).Equals( "CLICK" ) ) {
                adjustMode++;
                Console.WriteLine( "adjustMode " + adjustMode );
                //if( adjustMode % 2 == 0 ) {
                //    hands.StopTrackingAll( );

                //    UID = 0;
                //}

            }  
           

        }

        /**
         * This event is raised if the GestureGenerator starts to analyze a Gesture 
         *
         */
        void gesture_GestureIsRecording( object sender, GestureProgressEventArgs e ) {
            Console.WriteLine( "recording... " + e.Progress.ToString( ) + " " + e.Gesture );

        }

        int adjustMode = 0;
        Point3D lhLast;
        Point3D rhLast;
        Point3D lh;
        Point3D rh;
        float t = 0;
        System.Windows.Point p_TL;
        System.Windows.Point p_BR;

        void hands_HandsUpdated( object sender, HandUpdateEventArgs e ) {
            Dictionary<SkeletonJoint, SkeletonJointPosition> dict;
            //Console.WriteLine( "delta_t: " + ( e.Time - t ) );
            t = e.Time;
            if( adjustMode % 3 == 0 )
                return;

            /* only if we track the person */
            if( this.skeletonCapbility.IsTracking( e.UserID ) == false )
                return;

            try {
                GetJoints( e.UserID );
               dict = this.joints[ e.UserID ];

            } catch ( KeyNotFoundException knfEx ) {
                Console.Error.WriteLine( "Exeption " + knfEx.Message );
                hands.StopTracking( UID );
                throw new KeyNotFoundException( );

            }
            

            UID = e.UserID;

            try {
                lh = dict[ SkeletonJoint.LeftHand ].Position;
                rh = dict[ SkeletonJoint.RightHand ].Position;

            } catch( KeyNotFoundException knfEx ) {
                Console.Error.WriteLine( "Exeption " + knfEx.Message );
                hands.StopTracking( UID );
                throw new KeyNotFoundException( );

            }
            if( lhLast.X == 0.0 && lhLast.Y == 0.0 && lhLast.Z == 0.0 ) {
                lhLast = lh;
                if( rhLast.X == 0.0 && rhLast.Y == 0.0 && rhLast.Z == 0.0 ) {
                    rhLast = rh;
                
                }
                return;

            }

            float deltaX;
            float oldDeltaX;
            float deltaDeltaX;
            Mode dirX;
            
            if( ( rh.X <= ( p_BR.X ) ) && ( rh.X >= p_TL.X ) ) {
                if( ( lh.X <= ( p_BR.X ) ) && ( lh.X >= p_TL.X ) ) {
                    if( ( rh.Y >= p_TL.Y ) && ( rh.Y <= ( p_BR.Y ) ) ) {
                        if( ( lh.Y >= p_TL.Y ) && ( lh.Y <= ( p_BR.Y ) ) ) {
                            this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<double>( setFOVOpacity ), 0.3 );

                            deltaX = rh.X - lh.X;
                            oldDeltaX = rhLast.X - lhLast.X;
                            deltaDeltaX = oldDeltaX - deltaX;
                            rhLast = rh;
                            lhLast = lh;

                        } else {
                            this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<double>( setFOVOpacity ), 0.1 );
                            return;
                        }
                    } else {
                         this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<double>( setFOVOpacity ), 0.1 );
                        return;
                    }
                } else {
                     this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<double>( setFOVOpacity ), 0.1 );
                    return;
                }
            } else {
                 this.Dispatcher.Invoke( DispatcherPriority.Send, new Action<double>( setFOVOpacity ), 0.1 );
                return;
            }
            #region DELTAX - ADJUST LEVEL AND Threshold
            /* this checks if the users does not moved his hands */
            if( ( deltaDeltaX < 1.0 && deltaDeltaX > -1.0 ) )
                return;

            /* determine the speed we want to adjust the Images Level */
            if( oldDeltaX <= ( int ) Threshold.RESET ) {
                dirX = Mode.RESET;

            } else if( oldDeltaX >= ( int ) Threshold.FAST ) {
                dirX = oldDeltaX < deltaX ? Mode.UP_FAST : Mode.DOWN_FAST;
                if( dirX == Mode.UP_FAST )
                    Console.WriteLine( "Mode UP_FAST " );

            } else if( oldDeltaX >= ( int ) Threshold.NORMAL ) {
                dirX = oldDeltaX < deltaX ? Mode.UP : Mode.DOWN;
                if( dirX == Mode.UP )
                    Console.WriteLine( "Mode UP " );

            } else if( oldDeltaX >= ( int ) Threshold.SLOW ) {
                dirX = oldDeltaX < deltaX ? Mode.UP_SLOW : Mode.DOWN_SLOW;
                if( dirX == Mode.UP_SLOW )
                    Console.WriteLine( "Mode UP_SLOW " + oldDeltaX + " " + deltaX );

            } else {
                return;

            }
               
            if( ( adjustMode % 2 ) == 0 ) {
                /* notify the Windoow has changed */
                WindowChangedEventArgs argWindow = new WindowChangedEventArgs( "Window Changed", dirX );
                WindowChanged( this, argWindow );
                
            } else {
                LevelChangedEventArgs argLevel = new LevelChangedEventArgs( "Level Changed", dirX );
                LevelChanged( this, argLevel );
                
            }
            return;

            #endregion

        }

        void hands_HandDestroyed( object sender, HandUpdateEventArgs e ) {
            

        }
        /*****************************************************************************************/
        /* some rarely used globals */
        #region Rare global vars ****
        /* this is the xml-file for the project, containing some information */
        private readonly string SAMPLE_XML_FILE = @"../../data/ProjectConfig.xml";

        private Context context;
        private ScriptNode scriptNode;
        private DepthGenerator depth;
        private UserGenerator userGenerator;

        /* for tracking guestures we need the next two generators */
        private GestureGenerator gesture;
        private HandsGenerator hands;

        private SkeletonCapability skeletonCapbility;
        private PoseDetectionCapability poseDetectionCapability;
        private string calibPose;
        private int[] histogram;

        private Dictionary<int, Dictionary<SkeletonJoint, SkeletonJointPosition>> joints;

        private bool shouldDrawPixels = true;
        private bool shouldDrawBackground = true;
        private bool shouldPrintState = true;
        private const int skeletonBones = 16;

        private int ncolors = 6;

        private bool trackHands;
        #endregion(GLOBALS)

        /*****************************************************************************************/
        /* some structs we will need */
        #region Structures 
        struct UserSkeleton {
            public Line[] bones;
            public int ID;
            public bool isDisplayed;

            public UserSkeleton( int user ) {
                bones = new Line[ skeletonBones ];
                for( int i = 0; i < skeletonBones; i++ )
                    bones[i] = new Line( );

                foreach( Line _l in bones ) {
                        _l.X1 = 0;
                        _l.Y1 = 0;
                        _l.X2 = 0;
                        _l.Y2 = 0;
                        _l.Stroke = System.Windows.Media.Brushes.White;

                    }
                ID = user;
                isDisplayed = false;

            }
        }

        #endregion

        /*****************************************************************************************/
        /* some colors we will need later */
        #region Colors for painting 
        private System.Drawing.Color[] colors = { System.Drawing.Color.ForestGreen, 
                                                    System.Drawing.Color.Red, 
                                                    System.Drawing.Color.Blue, 
                                                    System.Drawing.Color.Yellow, 
                                                    System.Drawing.Color.Orange, 
                                                    System.Drawing.Color.Purple, 
                                                    System.Drawing.Color.White };

        private System.Drawing.Color[] anticolors = { System.Drawing.Color.Red, 
                                                        System.Drawing.Color.Green, 
                                                        System.Drawing.Color.Orange, 
                                                        System.Drawing.Color.Purple, 
                                                        System.Drawing.Color.Blue, 
                                                        System.Drawing.Color.Yellow, 
                                                        System.Drawing.Color.Black };
        private   MainWindow mainWindow;
        #endregion

        public event EventHandler<LevelChangedEventArgs> LevelChanged;
        public event EventHandler<WindowChangedEventArgs> WindowChanged;

    }

    public class LevelChangedEventArgs : EventArgs {
        public string text { get; set; }
        public Mode mode { get; set; }

        public LevelChangedEventArgs( string p, Mode dir ) {
            // TODO: Complete member initialization
            this.text = p;
            this.mode = dir;
        }

    }

    public class WindowChangedEventArgs : EventArgs {
        public string text { get; set; }
        public Mode mode { get; set; }

        public WindowChangedEventArgs( string p, Mode dir ) {
            this.text = p;
            this.mode = dir;
        }
    }

    /* contains speed and direction */
    public enum Mode { UP, DOWN, UP_SLOW, DOWN_SLOW, UP_FAST, DOWN_FAST, RESET }

    enum Threshold { RESET = 30, NORMAL = 60, SLOW = 100, FAST = 200 }

}
