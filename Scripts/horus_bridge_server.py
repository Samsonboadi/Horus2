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
import socket
import time

# Try to import Connection_settings module
try:
    from Connection_settings import connection_settings, external_data
    CONNECTION_SETTINGS_AVAILABLE = True
except ImportError as e:
    print(f"WARNING: Connection_settings module not available: {e}")
    CONNECTION_SETTINGS_AVAILABLE = False

# Try to import Horus modules
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

class DatabaseConnectionDiagnostics:
    """Enhanced database connection with diagnostics"""
    
    @staticmethod
    def test_network_connectivity(host, port, timeout=10):
        """Test basic network connectivity to database server"""
        try:
            logger.info(f"Testing network connectivity to {host}:{port}")
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(timeout)
            result = sock.connect_ex((host, int(port)))
            sock.close()
            
            if result == 0:
                logger.info("✓ Network connectivity: SUCCESS")
                return True, "Network connectivity successful"
            else:
                error_msg = f"Network connectivity failed (error code: {result})"
                logger.error(f"✗ {error_msg}")
                return False, error_msg
                
        except Exception as e:
            error_msg = f"Network connectivity test exception: {e}"
            logger.error(f"✗ {error_msg}")
            return False, error_msg
    
    @staticmethod
    def test_postgresql_response(host, port, timeout=5):
        """Test if PostgreSQL service is responding"""
        try:
            logger.info(f"Testing PostgreSQL service response on {host}:{port}")
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(timeout)
            sock.connect((host, int(port)))
            
            # Send a basic PostgreSQL startup message
            startup_msg = b'\x00\x00\x00\x08\x04\xd2\x16\x2f'
            sock.send(startup_msg)
            
            # Try to receive response
            response = sock.recv(1024)
            sock.close()
            
            if response:
                logger.info("✓ PostgreSQL service is responding")
                return True, "PostgreSQL service responding"
            else:
                error_msg = "PostgreSQL service not responding"
                logger.error(f"✗ {error_msg}")
                return False, error_msg
                
        except Exception as e:
            error_msg = f"PostgreSQL service test failed: {e}"
            logger.error(f"✗ {error_msg}")
            return False, error_msg
    
    @staticmethod
    def try_connection_methods(host, port, database, user, password):
        """Try multiple connection methods with detailed error reporting"""
        connection_methods = [
            {
                "name": "Standard Format",
                "conn_str": f"host={host} port={port} dbname={database} user={user} password={password} connect_timeout=15"
            },
            {
                "name": "Quoted Format",
                "conn_str": f"host='{host}' port='{port}' dbname='{database}' user='{user}' password='{password}' connect_timeout=15"
            },
            {
                "name": "URI Format",
                "conn_str": f"postgresql://{user}:{password}@{host}:{port}/{database}?connect_timeout=15"
            },
            {
                "name": "SSL Disabled",
                "conn_str": f"host={host} port={port} dbname={database} user={user} password={password} sslmode=disable connect_timeout=15"
            },
            {
                "name": "SSL Prefer",
                "conn_str": f"host={host} port={port} dbname={database} user={user} password={password} sslmode=prefer connect_timeout=15"
            }
        ]
        
        for method in connection_methods:
            logger.info(f"Trying {method['name']} connection method...")
            try:
                conn = psycopg2.connect(method['conn_str'])
                cursor = conn.cursor()
                cursor.execute("SELECT version(), current_database(), current_user")
                result = cursor.fetchone()
                cursor.close()
                conn.close()
                
                logger.info(f"✓ SUCCESS with {method['name']} method")
                logger.info(f"  Database: {result[1]}, User: {result[2]}")
                logger.info(f"  Version: {result[0][:60]}...")
                
                return conn, method['conn_str'], method['name']
                
            except psycopg2.OperationalError as op_ex:
                logger.warning(f"✗ {method['name']} failed - Operational: {op_ex}")
                
            except psycopg2.Error as pg_ex:
                logger.warning(f"✗ {method['name']} failed - PostgreSQL: {pg_ex}")
                
            except Exception as ex:
                logger.warning(f"✗ {method['name']} failed - General: {ex}")
        
        return None, None, None

class HorusMediaBridge:
    def __init__(self):
        self.client = None
        self.db_connection = None
        self.is_connected = False
        self.connection_string = None
        self.connection_method = None
    
        # Initialize default configuration
        self.db_config = {
            "host": "10.0.10.100",
            "port": "5432",
            "database": "HorusWebMoviePlayer",
            "user": "pocmsro",
            "password": ""  # Default to empty; will be overridden by Connection_settings if available
        }
    
        self.horus_config = {
            "url": "http://10.0.10.100:5050/web/",
            "host": "10.0.10.100",
            "port": 5050
        }
    
        # Load credentials from Connection_settings if available
        if CONNECTION_SETTINGS_AVAILABLE:
            try:
                settings = connection_settings(**external_data)
                self.db_config.update({
                    "host": settings.host,
                    "port": settings.port,
                    "database": settings.dbname,
                    "user": settings.dbuser,
                    "password": settings.password
                })
                logger.info(f"Loaded credentials from Connection_settings: host={settings.host}, user={settings.dbuser}, dbname={settings.dbname}")
            except Exception as e:
                logger.warning(f"Failed to load Connection_settings: {e}. Using default config.")

    def update_config(self, config_data):
        """Update configuration from external source"""
        try:
            if 'database' in config_data:
                self.db_config.update(config_data['database'])
                logger.info(f"Updated database config: host={self.db_config.get('host', 'not set')}, database={self.db_config.get('database', 'not set')}")
            
            if 'horus' in config_data:
                self.horus_config.update(config_data['horus'])
                if 'url' in config_data['horus']:
                    url = config_data['horus']['url']
                    if '://' in url:
                        url_parts = url.replace('http://', '').replace('https://', '').split(':')
                        if len(url_parts) >= 2:
                            self.horus_config['host'] = url_parts[0]
                            port_part = url_parts[1].split('/')[0]
                            try:
                                self.horus_config['port'] = int(port_part)
                            except ValueError:
                                logger.warning(f"Could not parse port from URL: {url}")
                
                logger.info(f"Updated Horus config: url={self.horus_config.get('url', 'not set')}")
                
            return True
        except Exception as e:
            logger.error(f"Failed to update config: {e}")
            return False

    def connect_database(self, db_config=None) -> bool:
        """Connect to database using the same connection string logic as horus_test_get_img.py"""
        try:
            if db_config:
                self.db_config.update(db_config)
        
            required_fields = ['host', 'database', 'user']
            missing_fields = [field for field in required_fields if not self.db_config.get(field)]
            if missing_fields:
                raise ValueError(f"Missing required database fields: {', '.join(missing_fields)}")
        
            logger.info(f"Attempting database connection to: {self.db_config['host']}:{self.db_config['port']}/{self.db_config['database']}")
        
            # Build connection string like horus_test_get_img.py
            db_params = [
                ("host", self.db_config['host']),
                ("port", self.db_config['port']),
                ("dbname", self.db_config['database']),
                ("user", self.db_config['user']),
                ("password", self.db_config['password']),
            ]
            connection_string = " ".join(map("=".join, filter(lambda x: x[1] is not None, db_params)))
        
            logger.info(f"Connection string: {connection_string.replace(self.db_config['password'], '***')}")
        
            self.db_connection = psycopg2.connect(connection_string)
            cursor = self.db_connection.cursor()
            cursor.execute("SELECT version(), current_database(), current_user")
            result = cursor.fetchone()
            cursor.close()
        
            logger.info(f"Database connection: SUCCESS - Connected to {result[1]} as {result[2]}")
            logger.info(f"PostgreSQL version: {result[0][:60]}...")
        
            self.connection_string = connection_string
            self.connection_method = "Standard Format (Dynamic)"
            return True
        
        except psycopg2.OperationalError as op_ex:
            logger.error(f"Database connection failed - Operational Error: {op_ex}")
            self.db_connection = None
            return False
        except psycopg2.Error as pg_ex:
            logger.error(f"Database connection failed - PostgreSQL Error: {pg_ex}")
            self.db_connection = None
            return False
        except ValueError as ve:
            logger.error(f"Database configuration error: {ve}")
            self.db_connection = None
            return False
        except Exception as e:
            logger.error(f"Database connection failed with unexpected error: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.db_connection = None
            return False
    
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
    
    def get_recordings(self) -> List[Dict]:
        """Get list of available recordings"""
        try:
            if not self.db_connection:
                logger.warning("No database connection for retrieving recordings")
                return []

            if not HORUS_AVAILABLE:
                logger.warning("Horus modules not available for retrieving recordings")
                return []

            logger.info("Starting to retrieve recordings from database...")
            
            recordings_manager = Recordings(self.db_connection)
            query_results = list(Recording.query(recordings_manager))
            
            logger.info(f"Horus ORM query returned {len(query_results)} recordings")
            
            recordings = []
            for i, recording in enumerate(query_results):
                try:
                    logger.info(f"Processing recording {i+1}: ID={recording.id}, Directory={getattr(recording, 'directory', 'N/A')}")
                    
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

# Global bridge instance
bridge = HorusMediaBridge()

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint with detailed diagnostics"""
    try:
        return jsonify({
            "status": "running",
            "timestamp": datetime.now().isoformat(),
            "horus_connected": bridge.is_connected,
            "database_connected": bridge.db_connection is not None,
            "horus_modules_available": HORUS_AVAILABLE,
            "connection_settings_available": CONNECTION_SETTINGS_AVAILABLE,
            "python_version": sys.version,
            "successful_connection_method": bridge.connection_method,
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
    """Connect endpoint with full diagnostics"""
    try:
        data = request.json
        logger.info("=" * 60)
        logger.info("ENHANCED CONNECTION PROCESS STARTING")
        logger.info("=" * 60)
        
        if data:
            safe_data = data.copy()
            if 'database' in safe_data and 'password' in safe_data['database']:
                safe_data['database']['password'] = '***'
            logger.info(f"Connection config received: {json.dumps(safe_data, indent=2)}")
        
        if data:
            success = bridge.update_config(data)
            if not success:
                return jsonify({
                    "success": False,
                    "error": "Failed to update configuration",
                    "horus_connected": False,
                    "database_connected": False
                }), 400
        
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
        
        logger.info("STEP 1: Enhanced database connection with diagnostics...")
        db_success = bridge.connect_database()
        logger.info(f"Database connection result: {'SUCCESS' if db_success else 'FAILED'}")
        
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
        
        result_message = f"Enhanced connection completed. Database: {'OK' if db_success else 'FAIL'}, Horus: {'OK' if horus_success else 'FAIL'}"
        
        troubleshooting_info = {}
        if not db_success:
            troubleshooting_info = {
                "network_accessible": "Check if host 10.0.10.100 is reachable",
                "port_open": "Verify PostgreSQL is listening on port 5432",
                "credentials": "Verify username/password are correct", 
                "pg_hba_conf": "Check PostgreSQL client authentication settings",
                "firewall": "Ensure no firewall blocking the connection"
            }
        
        logger.info("=" * 60)
        logger.info(f"ENHANCED CONNECTION RESULT: {result_message}")
        if bridge.connection_method:
            logger.info(f"Successful connection method: {bridge.connection_method}")
        logger.info("=" * 60)
        
        return jsonify({
            "success": True,
            "horus_connected": horus_success,
            "database_connected": db_success,
            "message": result_message,
            "horus_modules_available": HORUS_AVAILABLE,
            "connection_method": bridge.connection_method,
            "troubleshooting": troubleshooting_info if not db_success else None
        })
        
    except Exception as e:
        logger.error(f"Enhanced connection failed: {e}")
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
    """Test database connection with full diagnostics"""
    try:
        data = request.json or {}
        
        test_config = {
            "host": data.get('host') or bridge.db_config.get('host'),
            "port": data.get('port') or bridge.db_config.get('port'),
            "database": data.get('database') or bridge.db_config.get('database'),
            "user": data.get('user') or bridge.db_config.get('user'),
            "password": data.get('password') or bridge.db_config.get('password')
        }
        
        required_fields = ['host', 'database', 'user']
        missing_fields = [field for field in required_fields if not test_config.get(field)]
        
        if missing_fields:
            return jsonify({
                "success": False,
                "error": f"Missing required fields: {', '.join(missing_fields)}"
            }), 400
        
        logger.info("ENHANCED DATABASE TEST STARTING")
        logger.info(f"Testing: {test_config['host']}:{test_config['port']}/{test_config['database']}")
        
        network_ok, network_msg = DatabaseConnectionDiagnostics.test_network_connectivity(
            test_config['host'], test_config['port']
        )
        
        if not network_ok:
            return jsonify({
                "success": False,
                "error": f"Network connectivity failed: {network_msg}",
                "step_failed": "network_connectivity",
                "troubleshooting": [
                    f"Ping {test_config['host']} to test basic connectivity",
                    f"Check if port {test_config['port']} is open",
                    "Verify no firewall is blocking the connection",
                    "Ensure PostgreSQL server is running"
                ]
            }), 400
        
        service_ok, service_msg = DatabaseConnectionDiagnostics.test_postgresql_response(
            test_config['host'], test_config['port']
        )
        
        if not service_ok:
            return jsonify({
                "success": False,
                "error": f"PostgreSQL service not responding: {service_msg}",
                "step_failed": "postgresql_service",
                "troubleshooting": [
                    "Check postgresql.conf listen_addresses setting",
                    "Verify PostgreSQL is listening on the correct port",
                    "Check PostgreSQL service status",
                    "Review PostgreSQL error logs"
                ]
            }), 400
        
        conn, conn_str, method_name = DatabaseConnectionDiagnostics.try_connection_methods(
            test_config['host'], 
            test_config['port'],
            test_config['database'],
            test_config['user'],
            test_config['password']
        )
        
        if conn:
            cursor = conn.cursor()
            cursor.execute("SELECT version(), current_database(), current_user")
            db_info = cursor.fetchone()
            cursor.close()
            conn.close()
            
            logger.info("Enhanced database test: SUCCESS")
            
            return jsonify({
                "success": True,
                "message": "Database connection successful",
                "method": method_name,
                "database": db_info[1],
                "user": db_info[2],
                "version": db_info[0][:100] + "..." if len(db_info[0]) > 100 else db_info[0]
            })
        else:
            return jsonify({
                "success": False,
                "error": "Authentication failed with all connection methods",
                "step_failed": "authentication",
                "troubleshooting": [
                    "Verify username and password are correct",
                    "Check pg_hba.conf client authentication settings",
                    "Ensure user has CONNECT privilege on the database",
                    "Try connecting with: psql -h {host} -p {port} -U {user} -d {database}".format(**test_config)
                ]
            }), 400
        
    except Exception as e:
        logger.error(f"Enhanced database test failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e),
            "step_failed": "unexpected_error"
        }), 500

@app.route('/network-test', methods=['POST'])
def network_test():
    """Network connectivity test endpoint"""
    try:
        data = request.json or {}
        host = data.get('host', bridge.db_config.get('host', '10.0.10.100'))
        port = data.get('port', bridge.db_config.get('port', '5432'))
        
        logger.info(f"Network test requested for {host}:{port}")
        
        network_ok, network_msg = DatabaseConnectionDiagnostics.test_network_connectivity(host, port)
        service_ok, service_msg = DatabaseConnectionDiagnostics.test_postgresql_response(host, port)
        
        return jsonify({
            "success": network_ok and service_ok,
            "network_connectivity": {
                "success": network_ok,
                "message": network_msg
            },
            "postgresql_service": {
                "success": service_ok,
                "message": service_msg
            },
            "overall_status": "Ready for database connection" if (network_ok and service_ok) else "Network/Service issues detected"
        })
        
    except Exception as e:
        logger.error(f"Network test failed: {e}")
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
            bridge.connection_string = None
            bridge.connection_method = None
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
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/debug-db', methods=['GET'])
def debug_database():
    """Debug endpoint with comprehensive database diagnostics"""
    try:
        if not bridge.db_connection:
            return jsonify({
                "success": False,
                "error": "Not connected to database",
                "suggestion": "Use /connect or /test-db to establish connection first"
            }), 400
        
        cursor = bridge.db_connection.cursor()
        debug_info = {}
        
        cursor.execute("SELECT version(), current_database(), current_user")
        db_info = cursor.fetchone()
        debug_info['database_info'] = {
            "version": db_info[0],
            "database": db_info[1], 
            "user": db_info[2]
        }
        
        cursor.execute("""
            SELECT table_name, table_type
            FROM information_schema.tables 
            WHERE table_schema = 'public'
            ORDER BY table_name
        """)
        tables = cursor.fetchall()
        debug_info['tables'] = [{"name": t[0], "type": t[1]} for t in tables]
        
        horus_tables_info = {}
        for table_name in ['recordings', 'frames']:
            try:
                cursor.execute(f"SELECT COUNT(*) FROM {table_name}")
                count = cursor.fetchone()[0]
                horus_tables_info[table_name] = {"exists": True, "count": count}
                
                if count > 0:
                    cursor.execute(f"SELECT * FROM {table_name} LIMIT 3")
                    sample_data = cursor.fetchall()
                    cursor.execute(f"SELECT column_name FROM information_schema.columns WHERE table_name = '{table_name}'")
                    columns = [col[0] for col in cursor.fetchall()]
                    horus_tables_info[table_name]["columns"] = columns
                    horus_tables_info[table_name]["sample_rows"] = len(sample_data)
                    
            except psycopg2.Error as e:
                horus_tables_info[table_name] = {"exists": False, "error": str(e)}
        
        debug_info['horus_tables'] = horus_tables_info
        
        cursor.close()
        
        return jsonify({
            "success": True,
            "connection_method": bridge.connection_method,
            "database_debug": debug_info
        })
        
    except Exception as e:
        logger.error(f"Database debug failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.errorhandler(404)
def not_found(error):
    return jsonify({
        "success": False,
        "error": "Endpoint not found",
        "available_endpoints": [
            "GET /health - Health check & diagnostics",
            "POST /connect - Connect to services with full diagnostics", 
            "POST /test-db - Test database connection",
            "POST /network-test - Test network connectivity",
            "GET /debug-db - Debug database content",
            "GET /recordings - Get recordings list",
            "POST /images - Get images from recording",
            "POST /disconnect - Disconnect from services"
        ]
    }), 404

@app.errorhandler(500)
def internal_error(error):
    return jsonify({
        "success": False,
        "error": "Internal server error",
        "message": str(error)
    }), 500

def parse_arguments():
    """Parse command line arguments"""
    parser = argparse.ArgumentParser(description='Enhanced Horus Media Bridge Server')
    parser.add_argument('--config', help='Path to configuration JSON file')
    parser.add_argument('--port', type=int, default=5001, help='Server port (default: 5001)')
    parser.add_argument('--host', default='localhost', help='Server host (default: localhost)')
    parser.add_argument('--debug-network', action='store_true', help='Run network diagnostics on startup')
    return parser.parse_args()

def startup_network_diagnostics():
    """Run network diagnostics on startup"""
    print("")
    print("=" * 40)
    print("STARTUP NETWORK DIAGNOSTICS")
    print("=" * 40)
    
    db_host = bridge.db_config["host"]
    db_port = bridge.db_config["port"]
    
    network_ok, network_msg = DatabaseConnectionDiagnostics.test_network_connectivity(db_host, db_port)
    if network_ok:
        service_ok, service_msg = DatabaseConnectionDiagnostics.test_postgresql_response(db_host, db_port)
        if service_ok:
            print("✓ Network and PostgreSQL service tests passed")
            print("  Ready for database connections")
        else:
            print("⚠ Network OK but PostgreSQL service issues detected")
            print(f"  {service_msg}")
    else:
        print("✗ Network connectivity issues detected")
        print(f"  {network_msg}")
    
    print("=" * 40)

def check_dependencies():
    """Check if all required Python packages are available"""
    required_packages = {
        'flask': 'Flask',
        'psycopg2': 'psycopg2-binary', 
        'PIL': 'Pillow',
        'socket': 'Built-in'
    }
    
    missing_packages = []
    
    for package, install_name in required_packages.items():
        try:
            __import__(package)
            print(f"✓ {package}: Available")
        except ImportError:
            missing_packages.append(install_name)
            print(f"✗ {package}: Missing")
    
    if missing_packages:
        print(f"\nERROR: Missing required packages: {', '.join(missing_packages)}")
        print("Install them using:")
        print(f"conda install {' '.join(missing_packages)}")
        return False
    
    return True

if __name__ == '__main__':
    print("=" * 60)
    print("ENHANCED HORUS MEDIA BRIDGE SERVER")
    print("=" * 60)
    
    if not check_dependencies():
        print("Cannot start server due to missing dependencies")
        sys.exit(1)
    
    args = parse_arguments()
    print(f"Starting enhanced HTTP bridge server on http://{args.host}:{args.port}")
    
    if not HORUS_AVAILABLE:
        print("")
        print("WARNING: Horus modules are not available!")
        print("   Make sure the following Python packages are installed:")
        print("   - horus_media")
        print("   - horus_db") 
        print("   - horus_camera")
        print("")
    
    if args.debug_network or not HORUS_AVAILABLE:
        startup_network_diagnostics()
    
    if args.config:
        print(f"Loading configuration from: {args.config}")
        if bridge.update_config(json.load(open(args.config))):
            print("OK Configuration loaded successfully")
        else:
            print("ERROR Failed to load configuration, using defaults")
    
    print("")
    print("Enhanced endpoints:")
    print("  GET  /health                 - Health check & diagnostics")
    print("  POST /connect                - Connect with full diagnostics")
    print("  POST /test-db                - Test database connection")
    print("  POST /network-test           - Test network connectivity")
    print("  GET  /debug-db               - Debug database content")
    print("  GET  /recordings             - Get recordings list")
    print("  POST /images                 - Get images from recording")
    print("  POST /disconnect             - Disconnect from services")
    print("=" * 60)
    print("")
    
    try:
        app.run(
            host=args.host,
            port=args.port,
            debug=True,
            threaded=True,
            use_reloader=False
        )
    except KeyboardInterrupt:
        print("\nServer stopped by user")
    except Exception as e:
        print(f"Failed to start server: {e}")
        logger.error(f"Server startup failed: {e}")
        sys.exit(1)