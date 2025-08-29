"""
HTTP Bridge Server for Horus Media Client integration with C# ArcGIS Pro Add-in
This server provides a REST API that your C# application can call
"""

from flask import Flask, request, jsonify
import json
import logging
import traceback
from datetime import datetime
import base64
import io
import os
import sys
import argparse
from typing import List, Dict, Any
import itertools
from PIL import Image
import psycopg2

# Try to import your working Connection_settings module
try:
    from Connection_settings import connection_settings, external_data
    CONNECTION_SETTINGS_AVAILABLE = True
except ImportError as e:
    print(f"WARNING: Horus modules not available: {e}")
    print("The server will start but Horus functionality will be limited")
    HORUS_AVAILABLE = False# Try to import Horus modules - handle gracefully if they're not available
try:
    from horus_media import Client, Size
    from horus_db import Frames, Recordings, Frame, Recording
    from horus_camera import SphericalCamera
    HORUS_AVAILABLE = True
    print("OK Horus modules loaded successfully")
except ImportError as e:
    print(f"WARNING: Horus modules not available: {e}")
    print("The server will start but Horus functionality will be limited")
    HORUS_AVAILABLE = False

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

app = Flask(__name__)

class HorusMediaBridge:
    def __init__(self):
        self.client = None
        self.db_connection = None
        self.is_connected = False
        
        # Default configuration - will be updated from C# application
        self.db_config = {
            "host": "",
            "port": "5432",
            "database": "HorusWebMoviePlayer",
            "user": "",
            "password": ""
        }
        
        self.horus_config = {
            "url": "http://10.0.10.100:5050/web/",
            "host": "10.0.10.100",
            "port": 5050
        }

    def update_config(self, config_data):
        """Update configuration from external source (like C# application)"""
        try:
            if 'database' in config_data:
                self.db_config.update(config_data['database'])
                logger.info(f"Updated database config: host={self.db_config.get('host', 'not set')}, database={self.db_config.get('database', 'not set')}")
            
            if 'horus' in config_data:
                self.horus_config.update(config_data['horus'])
                # Extract host and port from URL if provided
                if 'url' in config_data['horus']:
                    url = config_data['horus']['url']
                    if '://' in url:
                        url_parts = url.replace('http://', '').replace('https://', '').split(':')
                        if len(url_parts) >= 2:
                            self.horus_config['host'] = url_parts[0]
                            port_part = url_parts[1].split('/')[0]  # Remove path after port
                            try:
                                self.horus_config['port'] = int(port_part)
                            except ValueError:
                                logger.warning(f"Could not parse port from URL: {url}")
                
                logger.info(f"Updated Horus config: url={self.horus_config.get('url', 'not set')}")
                
            return True
        except Exception as e:
            logger.error(f"Failed to update config: {e}")
            return False

    def load_config_from_file(self, config_path):
        """Load configuration from JSON file"""
        try:
            if os.path.exists(config_path):
                with open(config_path, 'r') as f:
                    config_data = json.load(f)
                    logger.info(f"Loaded config from file: {config_path}")
                    return self.update_config(config_data)
            else:
                logger.warning(f"Config file not found: {config_path}")
                return False
        except Exception as e:
            logger.error(f"Failed to load config from file: {e}")
            return False
        
    def get_database_connection_string(self):
        """Generate database connection string"""
        # Validate required parameters
        required_fields = ['host', 'database', 'user']
        for field in required_fields:
            if not self.db_config.get(field):
                raise ValueError(f"Database {field} is required but not provided")
        
        db_params = [
            ("host", self.db_config["host"]),
            ("port", self.db_config["port"]),
            ("dbname", self.db_config["database"]),
            ("user", self.db_config["user"]),
            ("password", self.db_config["password"]),
        ]
        return " ".join(map("=".join, filter(lambda x: x[1] is not None and x[1] != "", db_params)))
    
    def connect_horus(self, horus_url=None) -> bool:
        """Connect to Horus media server"""
        if not HORUS_AVAILABLE:
            logger.error("Horus modules are not available - cannot connect")
            return False
            
        try:
            url = horus_url or self.horus_config["url"]
            logger.info(f"Attempting to connect to Horus at: {url}")
            
            self.client = Client(url, timeout=20)
            self.client.attempts = 5
            
            # Test the connection by making a simple request
            # This will throw an exception if the connection fails
            test_response = self.client._session.get(url, timeout=10)
            if test_response.status_code == 200:
                self.is_connected = True
                logger.info(f"Horus connection: SUCCESS - {url}")
                return True
            else:
                logger.error(f"Horus connection failed: HTTP {test_response.status_code}")
                return False
            
        except Exception as e:
            logger.error(f"Failed to connect to Horus: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.is_connected = False
            return False
    
    def connect_database(self, db_config=None) -> bool:
        """Connect to PostgreSQL database using the same method as your working script"""
        try:
            if db_config:
                self.db_config.update(db_config)
            
            # Validate configuration
            required_fields = ['host', 'database', 'user']
            missing_fields = [field for field in required_fields if not self.db_config.get(field)]
            if missing_fields:
                raise ValueError(f"Missing required database fields: {', '.join(missing_fields)}")
            
            logger.info(f"Attempting database connection to: {self.db_config['host']}:{self.db_config['port']}/{self.db_config['database']}")
            
            # Method 1: Try using your Connection_settings approach if available
            if CONNECTION_SETTINGS_AVAILABLE:
                try:
                    logger.info("Trying Connection_settings module approach (like your working script)...")
                    
                    # Create external_data structure matching your script
                    external_data_bridge = {
                        'host': self.db_config['host'],
                        'port': self.db_config['port'],
                        'dbname': self.db_config['database'],
                        'dbuser': self.db_config['user'],
                        'password': self.db_config['password']
                    }
                    
                    # Use your connection_settings function
                    settings = connection_settings(**external_data_bridge)
                    
                    # Create connection string the same way as your working script
                    db_params = [
                        ("host", settings.host),
                        ("port", settings.port),
                        ("dbname", settings.dbname),
                        ("user", settings.dbuser),
                        ("password", settings.password),
                    ]
                    connection_string = " ".join(
                        map("=".join, filter(lambda x: x[1] is not None, db_params))
                    )
                    
                    logger.info("Connecting using Connection_settings pattern...")
                    self.db_connection = psycopg2.connect(connection_string)
                    
                    # Test connection like your script
                    cursor = self.db_connection.cursor()
                    cursor.execute("SELECT current_database(), current_user")
                    db_info = cursor.fetchone()
                    cursor.close()
                    
                    logger.info(f"Database connection: SUCCESS (Connection_settings method) - Connected to {db_info[0]} as {db_info[1]}")
                    return True
                    
                except Exception as settings_ex:
                    logger.warning(f"Connection_settings method failed: {settings_ex}")
                    if self.db_connection:
                        try:
                            self.db_connection.close()
                        except:
                            pass
                        self.db_connection = None
            
            # Method 2: Try exact connection string format from your working script
            try:
                logger.info("Trying exact connection string format from working script...")
                
                # Build connection string exactly like your manual script shows
                connection_string = f"host='{self.db_config['host']}' port='{self.db_config['port']}' dbname='{self.db_config['database']}' user='{self.db_config['user']}' password='{self.db_config['password']}'"
                logger.info(f"Using connection string format: {connection_string.replace(self.db_config['password'], '***')}")
                
                self.db_connection = psycopg2.connect(connection_string)
                
                # Test connection
                cursor = self.db_connection.cursor()
                cursor.execute("SELECT version()")
                version = cursor.fetchone()[0]
                cursor.close()
                
                logger.info(f"Database connection: SUCCESS (exact format method) - {version}")
                return True
                
            except Exception as exact_ex:
                logger.warning(f"Exact format method failed: {exact_ex}")
                if self.db_connection:
                    try:
                        self.db_connection.close()
                    except:
                        pass
                    self.db_connection = None
            
            # Method 3: Basic connection string (original approach)
            try:
                connection_string = self.get_database_connection_string()
                logger.info(f"Trying basic connection string approach...")
                self.db_connection = psycopg2.connect(connection_string)
                
                # Test connection
                cursor = self.db_connection.cursor()
                cursor.execute("SELECT 1")
                result = cursor.fetchone()
                cursor.close()
                
                if result and result[0] == 1:
                    logger.info("Database connection: SUCCESS (basic method)")
                    return True
                    
            except Exception as basic_ex:
                logger.warning(f"Basic connection method failed: {basic_ex}")
                if self.db_connection:
                    try:
                        self.db_connection.close()
                    except:
                        pass
                    self.db_connection = None
            
            # All methods failed
            logger.error("All database connection methods failed")
            return False
            
        except ValueError as ve:
            logger.error(f"Database configuration error: {ve}")
            self.db_connection = None
            return False
        except Exception as e:
            logger.error(f"Failed to connect to database: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.db_connection = None
            return False
    
    def get_recordings(self) -> List[Dict]:
        """Get list of available recordings using the same pattern as your working manual script"""
        try:
            if not self.db_connection:
                logger.warning("No database connection for retrieving recordings")
                return []

            if not HORUS_AVAILABLE:
                logger.warning("Horus modules not available for retrieving recordings")
                return []

            logger.info("Starting to retrieve recordings from database...")
            
            # First, let's check what tables exist and basic counts
            cursor = self.db_connection.cursor()
            try:
                cursor.execute("""
                    SELECT table_name 
                    FROM information_schema.tables 
                    WHERE table_schema = 'public' AND table_name = 'recordings'
                """)
                table_exists = cursor.fetchone()
                
                if not table_exists:
                    logger.error("recordings table does not exist in database")
                    return []
                
                # Check total count
                cursor.execute("SELECT COUNT(*) FROM recordings")
                recording_count = cursor.fetchone()[0]
                logger.info(f"Total recordings in database: {recording_count}")
                
                if recording_count == 0:
                    logger.warning("No recordings found in database")
                    return []
                
                # Get sample data to see structure
                cursor.execute("SELECT id, directory, created FROM recordings LIMIT 10")
                sample_recordings = cursor.fetchall()
                logger.info(f"Sample recordings from direct query: {sample_recordings}")
                
            finally:
                cursor.close()

            # Now use the Horus ORM approach that matches your working script
            recordings_manager = Recordings(self.db_connection)
            
            # Use the same query pattern as your working script
            # Your script uses: Recording.query(recordings, directory_like=endpoint)
            # Let's get ALL recordings first
            query_results = list(Recording.query(recordings_manager))
            
            logger.info(f"Horus ORM query returned {len(query_results)} recordings")
            
            recordings = []
            for i, recording in enumerate(query_results):
                try:
                    logger.info(f"Processing recording {i+1}: ID={recording.id}, Directory={getattr(recording, 'directory', 'N/A')}")
                    
                    # Get the setup for this recording (like in your working script)
                    recordings_manager.get_setup(recording)
                    
                    directory = getattr(recording, 'directory', f"Recording_{recording.id}")
                    created = getattr(recording, 'created', None)
                    
                    recordings.append({
                        "Id": str(recording.id),
                        "Endpoint": directory,
                        "Name": directory.split('\\')[-1] if directory else f"Recording {recording.id}",
                        "Description": f"Recording from {directory}",
                        "CreatedDate": created.isoformat() if created else None
                    })
                    
                except Exception as rec_ex:
                    logger.error(f"Error processing recording {i+1}: {rec_ex}")
                    continue
            
            logger.info(f"Successfully processed {len(recordings)} recordings")
            return recordings
                
        except Exception as e:
            logger.error(f"Failed to get recordings: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            return []
    
    def get_images(self, recording_endpoint: str, count: int = 5, width: int = 600, height: int = 600) -> List[Dict]:
        """Get images from a recording"""
        try:
            if not HORUS_AVAILABLE:
                raise Exception("Horus modules are not available")
                
            if not self.client or not self.is_connected or not self.db_connection:
                raise Exception("Not connected to Horus server or database")
            
            logger.info(f"Getting images from recording: {recording_endpoint}")
            
            recordings_manager = Recordings(self.db_connection)
            recording = next(Recording.query(recordings_manager, directory_like=recording_endpoint), None)
            if not recording:
                raise Exception(f"No recording found for endpoint: {recording_endpoint}")
            
            logger.info(f"Found recording ID: {recording.id}")
            recordings_manager.get_setup(recording)
            
            frames_manager = Frames(self.db_connection)
            frame_query = Frame.query(frames_manager, recordingid=recording.id, order_by="index")
            frames_list = list(itertools.islice(frame_query, count))
            
            logger.info(f"Found {len(frames_list)} frames")
            
            sp_camera = SphericalCamera()
            sp_camera.set_network_client(self.client)
            sp_camera.set_horizontal_fov(90)
            sp_camera.set_yaw(0)
            sp_camera.set_pitch(-30)
            
            processed_images = []
            for i, frame in enumerate(frames_list):
                try:
                    logger.info(f"Processing frame {i+1}/{len(frames_list)}")
                    communication = sp_camera.request(frame, Size(width, height))
                    image = communication.image
                    
                    buffer = io.BytesIO()
                    image.save(buffer, format="JPEG")
                    image_bytes = buffer.getvalue()
                    image_b64 = base64.b64encode(image_bytes).decode('utf-8')
                    
                    processed_images.append({
                        "Index": i,
                        "Data": image_b64,
                        "Format": "image/jpeg",
                        "Timestamp": frame.timestamp.isoformat() if frame.timestamp else None
                    })
                except Exception as frame_ex:
                    logger.error(f"Failed to process frame {i}: {frame_ex}")
                    continue
            
            logger.info(f"Successfully processed {len(processed_images)} images")
            return processed_images
            
        except Exception as e:
            logger.error(f"Failed to get images: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            raise
    
    def get_image_by_timestamp(self, recording_endpoint: str, timestamp: str, width: int = 600, height: int = 600) -> Dict:
        """Get specific image by timestamp"""
        try:
            if not HORUS_AVAILABLE:
                raise Exception("Horus modules are not available")
                
            if not self.client or not self.is_connected or not self.db_connection:
                raise Exception("Not connected to Horus server or database")
            
            recordings_manager = Recordings(self.db_connection)
            recording = next(Recording.query(recordings_manager, directory_like=recording_endpoint), None)
            if not recording:
                raise Exception(f"No recording found for endpoint: {recording_endpoint}")
            
            recordings_manager.get_setup(recording)
            
            frames_manager = Frames(self.db_connection)
            frame_query = Frame.query(frames_manager, recordingid=recording.id, order_by="timestamp")
            selected_frame = None
            target_time = datetime.fromisoformat(timestamp)
            min_diff = None
            for frame in frame_query:
                if frame.timestamp:
                    diff = abs(frame.timestamp - target_time)
                    if min_diff is None or diff < min_diff:
                        min_diff = diff
                        selected_frame = frame
            
            if not selected_frame:
                raise Exception(f"No frame found near timestamp: {timestamp}")
            
            sp_camera = SphericalCamera()
            sp_camera.set_network_client(self.client)
            sp_camera.set_horizontal_fov(90)
            sp_camera.set_yaw(0)
            sp_camera.set_pitch(-30)
            
            communication = sp_camera.request(selected_frame, Size(width, height))
            image = communication.image
            
            buffer = io.BytesIO()
            image.save(buffer, format="JPEG")
            image_bytes = buffer.getvalue()
            image_b64 = base64.b64encode(image_bytes).decode('utf-8')
            
            return {
                "Data": image_b64,
                "Format": "image/jpeg",
                "Timestamp": selected_frame.timestamp.isoformat() if selected_frame.timestamp else None
            }
                
        except Exception as e:
            logger.error(f"Failed to get image by timestamp: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            raise

# Global bridge instance
bridge = HorusMediaBridge()

@app.route('/health', methods=['GET'])
def health_check():
    """Enhanced health check endpoint with detailed diagnostics"""
    try:
        return jsonify({
            "status": "running",
            "timestamp": datetime.now().isoformat(),
            "horus_connected": bridge.is_connected,
            "database_connected": bridge.db_connection is not None,
            "horus_modules_available": HORUS_AVAILABLE,
            "python_version": sys.version,
            "config": {
                "db_host": bridge.db_config.get("host", "not set"),
                "db_port": bridge.db_config.get("port", "not set"),
                "db_database": bridge.db_config.get("database", "not set"),
                "db_user": bridge.db_config.get("user", "not set"),
                "db_password_set": bool(bridge.db_config.get("password")),
                "horus_url": bridge.horus_config.get("url", "not set"),
                "horus_host": bridge.horus_config.get("host", "not set"),
                "horus_port": bridge.horus_config.get("port", "not set")
            }
        })
    except Exception as e:
        logger.error(f"Health check error: {e}")
        return jsonify({
            "status": "error",
            "error": str(e),
            "timestamp": datetime.now().isoformat()
        }), 500

@app.route('/connect', methods=['POST'])
def connect():
    """Connect to Horus server and database with provided configuration"""
    try:
        data = request.json
        logger.info("=" * 50)
        logger.info("STARTING CONNECTION PROCESS")
        logger.info("=" * 50)
        
        # Log the configuration (without passwords)
        if data:
            safe_data = data.copy()
            if 'database' in safe_data and 'password' in safe_data['database']:
                safe_data['database']['password'] = '***'
            logger.info(f"Connection config received: {json.dumps(safe_data, indent=2)}")
        
        # Update configuration if provided
        if data:
            success = bridge.update_config(data)
            if not success:
                return jsonify({
                    "success": False,
                    "error": "Failed to update configuration",
                    "horus_connected": False,
                    "database_connected": False
                }), 400
        
        # Validate required database configuration
        db_required = ['host', 'database', 'user']
        missing_db_fields = [field for field in db_required if not bridge.db_config.get(field)]
        
        if missing_db_fields:
            error_msg = f"Missing required database fields: {', '.join(missing_db_fields)}"
            logger.error(error_msg)
            return jsonify({
                "success": False,
                "error": error_msg,
                "database_connected": False,
                "horus_connected": False
            }), 400
        
        # Connect to database first
        logger.info("STEP 1: Attempting database connection...")
        db_success = bridge.connect_database()
        logger.info(f"Database connection result: {'SUCCESS' if db_success else 'FAILED'}")
        
        # Connect to Horus
        horus_success = False
        if HORUS_AVAILABLE:
            if db_success:
                logger.info("STEP 2: Attempting Horus connection...")
                horus_url = bridge.horus_config.get('url')
                horus_success = bridge.connect_horus(horus_url)
                logger.info(f"Horus connection result: {'SUCCESS' if horus_success else 'FAILED'}")
            else:
                logger.warning("STEP 2: Skipping Horus connection - database connection failed")
        else:
            logger.warning("STEP 2: Skipping Horus connection - modules not available")
        
        result_message = f"Connection completed. Database: {'OK' if db_success else 'FAIL'}, Horus: {'OK' if horus_success else 'FAIL'}"
        logger.info("=" * 50)
        logger.info(f"CONNECTION RESULT: {result_message}")
        logger.info("=" * 50)
        
        return jsonify({
            "success": True,
            "horus_connected": horus_success,
            "database_connected": db_success,
            "message": result_message,
            "horus_modules_available": HORUS_AVAILABLE
        })
        
    except Exception as e:
        logger.error(f"Connection failed: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e),
            "traceback": traceback.format_exc(),
            "horus_connected": False,
            "database_connected": False
        }), 500

@app.route('/test-db', methods=['POST'])
def test_database_connection():
    """Test database connection without affecting main connection"""
    try:
        data = request.json or {}
        
        # Use provided config or current config
        test_config = {
            "host": data.get('host') or bridge.db_config.get('host'),
            "port": data.get('port') or bridge.db_config.get('port'),
            "database": data.get('database') or bridge.db_config.get('database'),
            "user": data.get('user') or bridge.db_config.get('user'),
            "password": data.get('password') or bridge.db_config.get('password')
        }
        
        # Validate required fields
        required_fields = ['host', 'database', 'user']
        missing_fields = [field for field in required_fields if not test_config.get(field)]
        
        if missing_fields:
            return jsonify({
                "success": False,
                "error": f"Missing required fields: {', '.join(missing_fields)}"
            }), 400
        
        # Build connection string
        db_params = [
            ("host", test_config["host"]),
            ("port", test_config["port"]),
            ("dbname", test_config["database"]),
            ("user", test_config["user"]),
            ("password", test_config["password"]),
        ]
        connection_string = " ".join(map("=".join, filter(lambda x: x[1] is not None and x[1] != "", db_params)))
        
        logger.info(f"Testing database connection to: {test_config['host']}:{test_config['port']}/{test_config['database']}")
        
        # Test connection
        test_connection = psycopg2.connect(connection_string)
        cursor = test_connection.cursor()
        cursor.execute("SELECT version()")
        db_version = cursor.fetchone()[0]
        cursor.close()
        test_connection.close()
        
        logger.info("Database test connection successful")
        
        return jsonify({
            "success": True,
            "message": "Database connection successful",
            "version": db_version
        })
        
    except psycopg2.Error as pg_ex:
        error_msg = f"PostgreSQL error: {pg_ex}"
        logger.error(error_msg)
        return jsonify({
            "success": False,
            "error": error_msg,
            "error_code": getattr(pg_ex, 'pgcode', 'Unknown'),
            "hint": "Check database credentials, network connectivity, and PostgreSQL server status"
        }), 400
        
    except Exception as e:
        logger.error(f"Database test failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/recordings', methods=['GET'])
def get_recordings():
    """Get list of available recordings"""
    try:
        if not HORUS_AVAILABLE:
            return jsonify({
                "Success": False,
                "Error": "Horus modules are not available"
            }), 503
        
        recordings = bridge.get_recordings()
        
        return jsonify({
            "Success": True,
            "Data": recordings,
            "Message": f"Retrieved {len(recordings)} recordings"
        })
        
    except Exception as e:
        logger.error(f"Failed to get recordings: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "Success": False,
            "Error": str(e)
        }), 500

@app.route('/images', methods=['POST'])
def get_images():
    """Get images from a recording"""
    try:
        if not HORUS_AVAILABLE:
            return jsonify({
                "Success": False,
                "Error": "Horus modules are not available"
            }), 503
            
        data = request.json
        logger.info(f"Received image request: {data}")
        
        recording_endpoint = data.get('recording_endpoint', 'Rotterdam360\\Ladybug5plus')
        count = data.get('count', 5)
        width = data.get('width', 600)
        height = data.get('height', 600)
        
        images = bridge.get_images(recording_endpoint, count, width, height)
        
        return jsonify({
            "Success": True,
            "Data": images,
            "Message": f"Retrieved {len(images)} images"
        })
        
    except Exception as e:
        logger.error(f"Failed to get images: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "Success": False,
            "Error": str(e)
        }), 500

@app.route('/image/<path:recording_endpoint>/<timestamp>', methods=['GET'])
def get_image_by_timestamp(recording_endpoint: str, timestamp: str):
    """Get specific image by timestamp"""
    try:
        if not HORUS_AVAILABLE:
            return jsonify({
                "Success": False,
                "Error": "Horus modules are not available"
            }), 503
            
        width = request.args.get('width', 600, type=int)
        height = request.args.get('height', 600, type=int)
        
        image_data = bridge.get_image_by_timestamp(recording_endpoint, timestamp, width, height)
        
        return jsonify({
            "Success": True,
            "Data": image_data
        })
        
    except Exception as e:
        logger.error(f"Failed to get image by timestamp: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "Success": False,
            "Error": str(e)
        }), 500

@app.route('/disconnect', methods=['POST'])
def disconnect():
    """Disconnect from services"""
    try:
        disconnected_services = []
        
        if bridge.client:
            bridge.client = None
            bridge.is_connected = False
            disconnected_services.append("Horus")
            logger.info("Disconnected from Horus")
        
        if bridge.db_connection:
            bridge.db_connection.close()
            bridge.db_connection = None
            disconnected_services.append("Database")
            logger.info("Disconnected from Database")
        
        message = f"Disconnected from: {', '.join(disconnected_services)}" if disconnected_services else "No active connections to disconnect"
        logger.info(message)
        
        return jsonify({
            "success": True,
            "message": message
        })
        
    except Exception as e:
        logger.error(f"Disconnect failed: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/debug-db', methods=['GET'])
def debug_database():
    """Debug endpoint to check database content"""
    try:
        if not bridge.db_connection:
            return jsonify({
                "success": False,
                "error": "Not connected to database"
            }), 400
        
        cursor = bridge.db_connection.cursor()
        
        # Check tables
        cursor.execute("""
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public'
            ORDER BY table_name
        """)
        tables = [table[0] for table in cursor.fetchall()]
        
        # Check recordings table specifically
        recordings_info = {}
        if 'recordings' in tables:
            cursor.execute("SELECT COUNT(*) FROM recordings")
            recordings_info['count'] = cursor.fetchone()[0]
            
            if recordings_info['count'] > 0:
                cursor.execute("SELECT id, directory, created FROM recordings LIMIT 10")
                recordings_info['samples'] = cursor.fetchall()
        
        cursor.close()
        
        return jsonify({
            "success": True,
            "database_info": {
                "tables": tables,
                "recordings": recordings_info
            }
        })
        
    except Exception as e:
        logger.error(f"Database debug failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500
def update_config():
    """Update bridge configuration"""
    try:
        data = request.json
        logger.info("Updating configuration...")
        success = bridge.update_config(data)
        
        return jsonify({
            "success": success,
            "message": "Configuration updated successfully" if success else "Failed to update configuration"
        })
        
    except Exception as e:
        logger.error(f"Config update failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

# Error handler for 404s
@app.errorhandler(404)
def not_found(error):
    return jsonify({
        "success": False,
        "error": "Endpoint not found",
        "available_endpoints": [
            "GET /health",
            "POST /connect", 
            "POST /test-db",
            "GET /recordings",
            "POST /images",
            "GET /image/<endpoint>/<timestamp>",
            "POST /disconnect",
            "POST /config"
        ]
    }), 404

# Error handler for 500s
@app.errorhandler(500)
def internal_error(error):
    return jsonify({
        "success": False,
        "error": "Internal server error",
        "message": str(error)
    }), 500

def parse_arguments():
    """Parse command line arguments"""
    parser = argparse.ArgumentParser(description='Horus Media Bridge Server')
    parser.add_argument('--config', help='Path to configuration JSON file')
    parser.add_argument('--port', type=int, default=5001, help='Server port (default: 5001)')
    parser.add_argument('--host', default='localhost', help='Server host (default: localhost)')
    return parser.parse_args()

def check_dependencies():
    """Check if all required Python packages are available"""
    required_packages = ['flask', 'psycopg2', 'PIL', 'json']
    missing_packages = []
    
    for package in required_packages:
        try:
            __import__(package)
        except ImportError:
            missing_packages.append(package)
    
    if missing_packages:
        print(f"ERROR: Missing required packages: {', '.join(missing_packages)}")
        print("Install them using:")
        print("conda install flask psycopg2-binary pillow")
        return False
    
    return True

if __name__ == '__main__':
    print("=" * 60)
    print("HORUS MEDIA BRIDGE SERVER")
    print("=" * 60)
    
    # Check dependencies first
    if not check_dependencies():
        print("Cannot start server due to missing dependencies")
        sys.exit(1)
    
    args = parse_arguments()
    print(f"Starting HTTP bridge server on http://{args.host}:{args.port}")
    
    # Check for Horus module availability
    if not HORUS_AVAILABLE:
        print("")
        print("WARNING: Horus modules are not available!")
        print("   Make sure the following Python packages are installed in your")
        print("   ArcGIS Pro Python environment:")
        print("   - horus_media")
        print("   - horus_db") 
        print("   - horus_camera")
        print("")
        print("   The server will start but Horus functionality will be limited")
        print("")
    
    # Load configuration from file if provided
    if args.config:
        print(f"Loading configuration from: {args.config}")
        if bridge.load_config_from_file(args.config):
            print("OK Configuration loaded successfully")
        else:
            print("ERROR Failed to load configuration, using defaults")
    
    print("")
    print("Available endpoints:")
    print("  GET  /health                 - Health check & diagnostics")
    print("  POST /connect                - Connect to services")
    print("  POST /test-db                - Test database connection")
    print("  GET  /debug-db               - Debug database content")
    print("  POST /config                 - Update configuration")
    print("  GET  /recordings             - Get recordings list")
    print("  POST /images                 - Get images from recording")
    print("  GET  /image/<endpoint>/<time> - Get image by timestamp")
    print("  POST /disconnect             - Disconnect from services")
    print("=" * 60)
    print("")
    
    try:
        # Start the Flask server
        app.run(
            host=args.host,
            port=args.port,
            debug=True,
            threaded=True,
            use_reloader=False  # Disable reloader to prevent duplicate startup
        )
    except KeyboardInterrupt:
        print("\nServer stopped by user")
    except Exception as e:
        print(f"Failed to start server: {e}")
        logger.error(f"Server startup failed: {e}")
        sys.exit(1)