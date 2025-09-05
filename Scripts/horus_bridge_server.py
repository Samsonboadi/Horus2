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

    def debug_received_config(self, data):
        """Debug the configuration received from C# application"""
        logger.info("=== DEBUGGING RECEIVED CONFIG FROM C# ===")
    
        if not data:
            logger.error("❌ No data received from C# application!")
            return False
    
        logger.info(f"Raw data type: {type(data)}")
        logger.info(f"Raw data keys: {list(data.keys()) if isinstance(data, dict) else 'Not a dict'}")
    
        if 'database' not in data:
            logger.error("❌ No 'database' section in received data!")
            logger.info(f"Available sections: {list(data.keys())}")
            return False
    
        db_section = data['database']
        logger.info(f"Database section type: {type(db_section)}")
        logger.info(f"Database section keys: {list(db_section.keys()) if isinstance(db_section, dict) else 'Not a dict'}")
    
        # Check each required field
        required_fields = ['host', 'port', 'database', 'user', 'password']
        for field in required_fields:
            value = db_section.get(field)
            logger.info(f"Field '{field}':")
            logger.info(f"  Present: {field in db_section}")
            logger.info(f"  Type: {type(value)}")
            logger.info(f"  Value: {'***' if field == 'password' and value else repr(value)}")
            if field == 'password' and value:
                logger.info(f"  Length: {len(value)}")
                logger.info(f"  First char: '{value[0]}' (ASCII: {ord(value[0])})")
                logger.info(f"  Last char: '{value[-1]}' (ASCII: {ord(value[-1])})")
                logger.info(f"  Contains $: {'$' in value}")
                logger.info(f"  Contains %: {'%' in value}")
                logger.info(f"  All chars visible: {all(32 <= ord(c) <= 126 for c in value)}")
    
        logger.info("=== END CONFIG DEBUG ===")
        return True

    def test_received_password_immediately(self, password):
        """Test the received password immediately to verify it works"""
        if not password:
            logger.error("❌ Password is empty/None - cannot test")
            return False
    
        logger.info("=== TESTING RECEIVED PASSWORD IMMEDIATELY ===")
    
        try:
            # Test with the exact same method that worked in diagnosis
            test_conn_str = f"host={self.db_config['host']} port={self.db_config['port']} dbname={self.db_config['database']} user={self.db_config['user']} password={password}"
        
            logger.info("Testing received password with working method...")
            test_conn = psycopg2.connect(test_conn_str)
        
            cursor = test_conn.cursor()
            cursor.execute("SELECT current_user, current_database()")
            result = cursor.fetchone()
            cursor.close()
            test_conn.close()
        
            logger.info(f"✅ SUCCESS: Received password works! Connected as {result[0]} to {result[1]}")
            return True
        
        except Exception as e:
            logger.error(f"❌ FAILED: Received password doesn't work - {e}")
            logger.error("This means the password was corrupted during transfer from C#")
        
            # Try some common corruption fixes
            logger.info("Trying corruption fixes...")
        
            fixes = [
                ("Stripped", password.strip()),
                ("No trailing nulls", password.rstrip('\x00')),
                ("URL decoded", password.replace('%25', '%').replace('%24', '$')),
                ("Double-decode", password.replace('%2525', '%').replace('%2524', '$'))
            ]
        
            for fix_name, fixed_password in fixes:
                try:
                    test_conn_str = f"host={self.db_config['host']} port={self.db_config['port']} dbname={self.db_config['database']} user={self.db_config['user']} password={fixed_password}"
                    test_conn = psycopg2.connect(test_conn_str)
                    test_conn.close()
                    logger.info(f"✅ SUCCESS with {fix_name}: '{fixed_password}'")
                    # Update the stored password with the working version
                    self.db_config['password'] = fixed_password
                    return True
                except Exception as fix_e:
                    logger.info(f"❌ {fix_name} didn't work: {str(fix_e)[:50]}...")
        
            return False

    def update_config(self, config_data):
        """Enhanced config update with detailed logging"""
        try:
            logger.info("📝 Updating bridge configuration...")
        
            if 'database' in config_data:
                old_password = self.db_config.get('password', '')
                self.db_config.update(config_data['database'])
                new_password = self.db_config.get('password', '')
            
                logger.info(f"Database config updated:")
                logger.info(f"  host: {self.db_config.get('host')}")
                logger.info(f"  port: {self.db_config.get('port')}")
                logger.info(f"  database: {self.db_config.get('database')}")
                logger.info(f"  user: {self.db_config.get('user')}")
                logger.info(f"  password changed: {old_password != new_password}")
                logger.info(f"  password length: {len(new_password) if new_password else 0}")
        
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
        """Connect to Horus media server - FIXED VERSION"""
        if not HORUS_AVAILABLE:
            logger.error("Horus modules are not available - cannot connect")
            return False
            
        try:
            url = horus_url or self.horus_config["url"]
            logger.info(f"Creating Horus client for: {url}")
            
            # FIXED: Just create the client without testing _session
            self.client = Client(url, timeout=20)
            self.client.attempts = 5
            
            # If we got here without exception, consider it successful
            self.is_connected = True
            logger.info(f"Horus client created successfully")
            return True
            
        except Exception as e:
            logger.error(f"Failed to create Horus client: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.is_connected = False
            return False
    
    def debug_recording_attributes(self, recording, index):
        """Debug what attributes a Recording object actually has"""
        logger.info(f"=== DEBUGGING RECORDING {index} ATTRIBUTES ===")
    
        try:
            # Get all attributes
            all_attrs = dir(recording)
            logger.info(f"All attributes: {all_attrs}")
        
            # Check for common attributes
            common_attrs = ['id', 'directory', 'created', 'created_at', 'timestamp', 'name', 'setup']
            for attr in common_attrs:
                if hasattr(recording, attr):
                    value = getattr(recording, attr)
                    logger.info(f"  {attr}: {value} (type: {type(value)})")
                else:
                    logger.info(f"  {attr}: NOT PRESENT")
        
            # Check for any date-related attributes
            date_attrs = [attr for attr in all_attrs if 'date' in attr.lower() or 'time' in attr.lower() or 'created' in attr.lower()]
            logger.info(f"Date-related attributes: {date_attrs}")
        
        except Exception as debug_ex:
            logger.error(f"Debug recording attributes failed: {debug_ex}")
    
        logger.info("=== END RECORDING DEBUG ===")

    def get_recordings(self) -> List[Dict]:
        """Get recordings with enhanced debugging - FIXED VERSION"""
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
        
            # Debug the first recording to understand the structure
            if query_results:
                logger.info("Debugging first recording to understand structure...")
                self.debug_recording_attributes(query_results[0], 1)
        
            recordings = []
            processed_count = 0
        
            for i, recording in enumerate(query_results):
                try:
                    logger.info(f"Processing recording {i+1}: ID={recording.id}")
                
                    recordings_manager.get_setup(recording)
                
                    directory = getattr(recording, 'directory', f"Recording_{recording.id}")
                
                    # Use safe attribute access - NO MORE 'created' attribute errors
                    name = directory.split('\\')[-1] if directory else f"Recording {recording.id}"
                    description = f"Recording from {directory}" if directory else f"Recording ID {recording.id}"
                
                    recording_info = {
                        "Id": str(recording.id),
                        "Endpoint": directory,
                        "Name": name,
                        "Description": description,
                        "CreatedDate": None  # Set to None since 'created' attribute doesn't exist
                    }
                
                    recordings.append(recording_info)
                    processed_count += 1
                
                    if processed_count <= 5:  # Only log details for first 5
                        logger.info(f"✅ Processed recording {i+1}: {name}")
                
                except Exception as rec_ex:
                    logger.error(f"❌ Error processing recording {i+1}: {rec_ex}")
                    continue
        
            logger.info(f"Successfully processed {processed_count} out of {len(query_results)} recordings")
            return recordings
            
        except Exception as e:
            logger.error(f"Failed to get recordings: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            return []
    


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
    """Enhanced connect endpoint with detailed JSON transfer debugging"""
    try:
        data = request.json
        logger.info("=" * 80)
        logger.info("🔍 ENHANCED CONNECTION WITH JSON TRANSFER DEBUGGING")
        logger.info("=" * 80)
        
        # Step 1: Debug what we received from C#
        logger.info("STEP 1: Analyzing data received from C# application...")
        
        if not bridge.debug_received_config(data):
            return jsonify({
                "success": False,
                "error": "Invalid configuration received from C# application",
                "debug_info": {
                    "data_received": data is not None,
                    "data_type": str(type(data)),
                    "data_keys": list(data.keys()) if isinstance(data, dict) else None
                }
            }), 400
        
        # Step 2: Update configuration
        logger.info("STEP 2: Updating bridge configuration...")
        if data:
            success = bridge.update_config(data)
            if not success:
                return jsonify({
                    "success": False,
                    "error": "Failed to update bridge configuration",
                    "horus_connected": False,
                    "database_connected": False
                }), 400
        
        # Step 3: Test the received password immediately
        logger.info("STEP 3: Testing received password immediately...")
        received_password = bridge.db_config.get('password')
        
        if not received_password:
            logger.error("❌ CRITICAL: No password received from C#!")
            
            # Try to use Connection_settings as fallback
            if CONNECTION_SETTINGS_AVAILABLE:
                try:
                    from Connection_settings import connection_settings, external_data
                    settings = connection_settings(**external_data)
                    if settings.password:
                        logger.info("✅ Using fallback password from Connection_settings")
                        bridge.db_config['password'] = settings.password
                        received_password = settings.password
                    else:
                        logger.error("❌ Connection_settings also has no password")
                except Exception as e:
                    logger.error(f"❌ Failed to load fallback password: {e}")
            
            if not received_password:
                return jsonify({
                    "success": False,
                    "error": "No password received from C# application and no fallback available",
                    "debug_info": {
                        "password_in_json": 'database' in data and 'password' in data.get('database', {}),
                        "password_value": repr(data.get('database', {}).get('password')),
                        "connection_settings_available": CONNECTION_SETTINGS_AVAILABLE
                    }
                }), 400
        
        # Test the password immediately
        password_works = bridge.test_received_password_immediately(received_password)
        
        if not password_works:
            return jsonify({
                "success": False,
                "error": "Password received from C# application doesn't work",
                "debug_info": {
                    "password_length": len(received_password),
                    "password_type": str(type(received_password)),
                    "password_sample": f"{received_password[:2]}***{received_password[-2:]}",
                    "contains_special_chars": any(c in received_password for c in '$%@#&*'),
                    "troubleshooting": "The password was likely corrupted during JSON transfer from C#"
                }
            }), 400
        
        # Step 4: Proceed with database connection
        logger.info("STEP 4: Proceeding with database connection using verified password...")
        
        # Check all required fields one more time
        required_fields = ['host', 'port', 'database', 'user', 'password']
        missing_fields = [field for field in required_fields if not bridge.db_config.get(field)]
        
        if missing_fields:
            return jsonify({
                "success": False,
                "error": f"Missing required fields: {', '.join(missing_fields)}",
                "debug_info": {field: bridge.db_config.get(field) for field in required_fields}
            }), 400
        
        # Now try the actual database connection
        logger.info(f"Connecting to database: {bridge.db_config['user']}@{bridge.db_config['host']}:{bridge.db_config['port']}/{bridge.db_config['database']}")
        
        db_success = bridge.connect_database()
        logger.info(f"Database connection result: {'✅ SUCCESS' if db_success else '❌ FAILED'}")
        
        # Step 5: Try Horus connection if database succeeded
        horus_success = False
        if HORUS_AVAILABLE and db_success:
            logger.info("STEP 5: Attempting Horus connection...")
            horus_url = bridge.horus_config.get('url')
            horus_success = bridge.connect_horus(horus_url)
            logger.info(f"Horus connection result: {'SUCCESS' if horus_success else 'FAILED'}")
        else:
            logger.info("STEP 5: Skipping Horus connection (database failed or modules unavailable)")
        
        # Final result
        result_message = f"Connection completed. Database: {'OK' if db_success else 'FAIL'}, Horus: {'OK' if horus_success else 'FAIL'}"
        
        logger.info("=" * 80)
        logger.info(f"FINAL RESULT: {result_message}")
        logger.info("=" * 80)
        
        return jsonify({
            "success": True,
            "horus_connected": horus_success,
            "database_connected": db_success,
            "message": result_message,
            "debug_info": {
                "password_test_passed": password_works,
                "connection_method": bridge.connection_method,
                "horus_modules_available": HORUS_AVAILABLE
            }
        })
        
    except Exception as e:
        logger.error(f"Connection process failed: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e),
            "debug_info": {
                "traceback": traceback.format_exc()[:500]
            }
        }), 500

@app.route('/debug-json', methods=['POST'])
def debug_json_from_csharp():
    """Debug endpoint to see exactly what C# is sending"""
    try:
        logger.info("DEBUG-JSON ENDPOINT CALLED")
        logger.info("=" * 60)
        
        # Get raw request data
        raw_data = request.get_data()
        logger.info(f"Raw request data length: {len(raw_data)} bytes")
        logger.info(f"Raw request data type: {type(raw_data)}")
        logger.info(f"Raw data (first 200 chars): {raw_data[:200]}")
        
        # Get content type
        content_type = request.content_type
        logger.info(f"Content-Type header: {content_type}")
        
        # Try to parse as JSON
        try:
            json_data = request.json
            logger.info(f"JSON parsing: SUCCESS")
            logger.info(f"JSON data type: {type(json_data)}")
            
            if isinstance(json_data, dict):
                logger.info(f"JSON keys: {list(json_data.keys())}")
                
                if 'database' in json_data:
                    db_data = json_data['database']
                    logger.info(f"Database section type: {type(db_data)}")
                    logger.info(f"Database keys: {list(db_data.keys()) if isinstance(db_data, dict) else 'Not a dict'}")
                    
                    if 'password' in db_data:
                        password = db_data['password']
                        logger.info(f"PASSWORD ANALYSIS:")
                        logger.info(f"  Type: {type(password)}")
                        logger.info(f"  Length: {len(password) if password else 0}")
                        logger.info(f"  Is None: {password is None}")
                        logger.info(f"  Is empty string: {password == ''}")
                        logger.info(f"  Repr: {repr(password)}")
                        
                        if password:
                            logger.info(f"  First 3 chars: '{password[:3]}'")
                            logger.info(f"  Last 3 chars: '{password[-3:]}'")
                            logger.info(f"  ASCII values: {[ord(c) for c in password[:5]]}")
                            logger.info(f"  Contains $: {'$' in password}")  # FIXED: Added missing $
                            logger.info(f"  Contains %: {'%' in password}")
                            logger.info(f"  Expected password: {'ZSE$%67ujm' == password}")
                            
                            # Test the password immediately
                            try:
                                test_conn = psycopg2.connect(
                                    host="10.0.10.100",
                                    port=5432,
                                    database="HorusWebMoviePlayer",
                                    user="pocmsro",
                                    password=password
                                )
                                test_conn.close()
                                logger.info(f"  PASSWORD WORKS!")
                            except Exception as pw_test_error:
                                logger.error(f"  PASSWORD FAILED: {pw_test_error}")
                    else:
                        logger.error("No password field in database section!")
                else:
                    logger.error("No database section in JSON!")
            else:
                logger.error(f"JSON data is not a dict: {json_data}")
                
        except Exception as json_error:
            logger.error(f"JSON parsing FAILED: {json_error}")
            logger.info(f"Raw text attempt: {raw_data.decode('utf-8', errors='replace')[:500]}")
        
        logger.info("=" * 60)
        
        return jsonify({
            "success": True,
            "message": "Debug data logged to console",
            "received_data_length": len(raw_data),
            "content_type": content_type,
            "timestamp": datetime.now().isoformat()
        })
        
    except Exception as e:
        logger.error(f"Debug endpoint failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/test-password', methods=['POST'])
def test_password_directly():
    """Test a password directly without going through the full connection process"""
    try:
        data = request.json
        
        if not data or 'password' not in data:
            return jsonify({
                "success": False,
                "error": "No password provided in request"
            }), 400
        
        password = data['password']
        host = data.get('host', '10.0.10.100')
        port = data.get('port', 5432)
        database = data.get('database', 'HorusWebMoviePlayer') 
        user = data.get('user', 'pocmsro')
        
        logger.info(f"Testing password directly: {user}@{host}:{port}/{database}")
        logger.info(f"Password length: {len(password)}")
        logger.info(f"Password sample: {password[:2]}***{password[-2:]}")
        
        try:
            test_conn = psycopg2.connect(
                host=host,
                port=int(port),
                database=database,
                user=user,
                password=password,
                connect_timeout=10
            )
            
            cursor = test_conn.cursor()
            cursor.execute("SELECT current_user, current_database(), version()")
            result = cursor.fetchone()
            cursor.close()
            test_conn.close()
            
            logger.info(f"Password test SUCCESS: {result[0]}@{result[1]}")
            
            return jsonify({
                "success": True,
                "message": "Password works correctly",
                "connection_info": {
                    "user": result[0],
                    "database": result[1],
                    "version": result[2][:50] + "..."
                }
            })
            
        except Exception as conn_error:
            logger.error(f"Password test FAILED: {conn_error}")
            
            return jsonify({
                "success": False,
                "error": f"Password test failed: {conn_error}",
                "password_analysis": {
                    "length": len(password),
                    "type": str(type(password)),
                    "contains_special": any(c in password for c in '$%@#&*'),
                    "first_char_ascii": ord(password[0]) if password else None,
                    "last_char_ascii": ord(password[-1]) if password else None
                }
            })
            
    except Exception as e:
        logger.error(f"Test password endpoint failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
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
                "step_failed": "network_connectivity"
            }), 400
        
        service_ok, service_msg = DatabaseConnectionDiagnostics.test_postgresql_response(
            test_config['host'], test_config['port']
        )
        
        if not service_ok:
            return jsonify({
                "success": False,
                "error": f"PostgreSQL service not responding: {service_msg}",
                "step_failed": "postgresql_service"
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
                "step_failed": "authentication"
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
    """FIXED: Get list of available recordings - removed self parameter"""
    try:
        return jsonify({
            "Success": True,
            "Data": bridge.get_recordings(),
            "Message": f"Retrieved recordings successfully"
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
    """Get images from a recording - FIXED to handle BytesIO return from get_image"""
    try:
        if not HORUS_AVAILABLE:
            return jsonify({
                "Success": False,
                "Error": "Horus modules are not available"
            }), 503
            
        data = request.json
        logger.info(f"Received image request: {data}")
        
        recording_endpoint = data.get('recording_endpoint', 'Rotterdam360\\Ladybug5plus')
        count = data.get('count', 20) 
        width = data.get('width', 1920)
        height = data.get('height', 1080)
        
        logger.info(f"Getting images from recording: {recording_endpoint}")
        
        if not bridge.client or not bridge.is_connected or not bridge.db_connection:
            return jsonify({
                "Success": False,
                "Error": "Not connected to Horus server or database"
            }), 500
        
        recordings = Recordings(bridge.db_connection)
        
        # Try different approaches to find the recording
        recording = None
        search_attempts = []
        
        # Method 1: Try with the endpoint as-is (full path)
        try:
            logger.info(f"Trying directory_like with full path: {recording_endpoint}")
            recording = next(Recording.query(recordings, directory_like=recording_endpoint), None)
            if recording:
                search_attempts.append(f"SUCCESS: Full path '{recording_endpoint}'")
            else:
                search_attempts.append(f"FAILED: Full path '{recording_endpoint}'")
        except Exception as e:
            search_attempts.append(f"ERROR: Full path '{recording_endpoint}' - {e}")
        
        # Method 2: Try with just the directory name
        if not recording:
            try:
                dir_name = recording_endpoint.split('\\')[-1]
                logger.info(f"Trying directory_like with dir name: {dir_name}")
                recording = next(Recording.query(recordings, directory_like=dir_name), None)
                if recording:
                    search_attempts.append(f"SUCCESS: Directory name '{dir_name}'")
                else:
                    search_attempts.append(f"FAILED: Directory name '{dir_name}'")
            except Exception as e:
                search_attempts.append(f"ERROR: Directory name '{dir_name}' - {e}")
        
        # Method 3: Try with double backslashes
        if not recording:
            try:
                double_slash_endpoint = recording_endpoint.replace('\\', '\\\\')
                logger.info(f"Trying with double backslashes: {double_slash_endpoint}")
                recording = next(Recording.query(recordings, directory_like=double_slash_endpoint), None)
                if recording:
                    search_attempts.append(f"SUCCESS: Double backslashes '{double_slash_endpoint}'")
                else:
                    search_attempts.append(f"FAILED: Double backslashes '{double_slash_endpoint}'")
            except Exception as e:
                search_attempts.append(f"ERROR: Double backslashes '{double_slash_endpoint}' - {e}")
        
        # Method 4: Try partial matching
        if not recording:
            try:
                logger.info("Trying partial matching...")
                all_recordings = list(Recording.query(recordings))
                target_name = recording_endpoint.split('\\')[-1].lower()
                
                for rec in all_recordings:
                    try:
                        recordings.get_setup(rec)
                        if hasattr(rec, 'directory') and rec.directory:
                            rec_name = rec.directory.split('\\')[-1].lower()
                            if target_name in rec_name or rec_name in target_name:
                                recording = rec
                                search_attempts.append(f"SUCCESS: Partial match '{target_name}' -> '{rec.directory}'")
                                break
                    except Exception as setup_ex:
                        continue
                
                if not recording:
                    search_attempts.append(f"FAILED: Partial matching for '{target_name}'")
            except Exception as e:
                search_attempts.append(f"ERROR: Partial matching - {e}")
        
        logger.info("Recording search attempts:")
        for attempt in search_attempts:
            logger.info(f"  {attempt}")
        
        if not recording:
            logger.error(f"No recording found after all search methods")
            try:
                logger.info("Available recordings (first 10):")
                all_recordings = list(Recording.query(recordings))
                for i, rec in enumerate(all_recordings[:10]):
                    try:
                        recordings.get_setup(rec)
                        directory = getattr(rec, 'directory', 'No directory')
                        logger.info(f"  [{i+1}] ID={rec.id}: {directory}")
                    except:
                        logger.info(f"  [{i+1}] ID={rec.id}: <setup failed>")
                if len(all_recordings) > 10:
                    logger.info(f"  ... and {len(all_recordings) - 10} more")
            except Exception as debug_ex:
                logger.error(f"Failed to list recordings: {debug_ex}")
            
            return jsonify({
                "Success": False,
                "Error": f"No recording found for endpoint: {recording_endpoint}",
                "Debug": {
                    "search_attempts": search_attempts,
                    "endpoint_received": recording_endpoint
                }
            }), 404
        
        recordings.get_setup(recording)
        logger.info(f"Found recording: {recording} -> {recording.directory}")
        logger.info(f"Recording setup: {getattr(recording, 'setup', 'No setup info')}")
        
        # Create spherical camera exactly like standalone script
        sp_camera = SphericalCamera()
        sp_camera.set_network_client(bridge.client)
        sp_camera.set_horizontal_fov(90)  # Default value
        sp_camera.set_yaw(0)
        sp_camera.set_pitch(-30)
        
        # Get frames exactly like standalone script
        frames = Frames(bridge.db_connection)
        temp = Frame.query(frames, recordingid=recording.id, order_by="index")
        temp = list(itertools.islice(temp, int(count)))
        
        logger.info(f"Found {len(temp)} frames for processing")
        
        if len(temp) == 0:
            logger.warning("No frames found for this recording")
            return jsonify({
                "Success": True,
                "Data": [],
                "Message": "No frames found for this recording"
            })
        
        processed_images = []
        
        # Process each frame
        for i, t in enumerate(temp):
            try:
                logger.info(f"Processing frame {i+1}/{len(temp)}: {t}")
                
                results = Frame.query(frames, recordingid=t.recordingid, index=t.index, order_by="index")
                frame = next(results)
                
                logger.info(f"Got frame: {frame}")
                
                if frame is None:
                    logger.warning(f"Frame {i+1} is None, skipping")
                    continue
                
                logger.info(f"Frame location: {frame.get_location() if hasattr(frame, 'get_location') else 'No location method'}")
                
                # Set frame and acquire spherical image
                sp_camera.set_frame(recording, frame)
                spherical_image = sp_camera.acquire(Size(width, height), manual_fetch=False)
                
                if spherical_image is None:
                    logger.error(f"Failed to acquire image for frame {i+1}: SphericalImage is None")
                    continue
                
                # FIXED: Handle different return types from get_image()
                try:
                    image_data = spherical_image.get_image()
                    logger.info(f"get_image() returned type: {type(image_data)}")
                    
                    # If get_image() returns BytesIO, use it directly
                    if isinstance(image_data, io.BytesIO):
                        logger.info(f"get_image() returned BytesIO, using directly")
                        image_data.seek(0)  # Make sure we're at the beginning
                        image_bytes = image_data.getvalue()
                        
                        # Convert to base64
                        image_b64 = base64.b64encode(image_bytes).decode('utf-8')
                        
                        processed_images.append({
                            "Index": i,
                            "Data": image_b64,
                            "Format": "image/jpeg",
                            "Timestamp": frame.timestamp.isoformat() if hasattr(frame, 'timestamp') and frame.timestamp else None
                        })
                        
                        logger.info(f"Successfully processed frame {i+1} (BytesIO direct)")
                    
                    # If get_image() returns bytes, use directly
                    elif isinstance(image_data, bytes):
                        logger.info(f"get_image() returned bytes, using directly")
                        image_b64 = base64.b64encode(image_data).decode('utf-8')
                        
                        processed_images.append({
                            "Index": i,
                            "Data": image_b64,
                            "Format": "image/jpeg", 
                            "Timestamp": frame.timestamp.isoformat() if hasattr(frame, 'timestamp') and frame.timestamp else None
                        })
                        
                        logger.info(f"Successfully processed frame {i+1} (bytes direct)")
                    
                    # If get_image() returns PIL Image, convert to bytes
                    elif hasattr(image_data, 'save'):  # PIL Image check
                        logger.info(f"get_image() returned PIL Image, converting to bytes")
                        buffer = io.BytesIO()
                        image_data.save(buffer, format="JPEG", quality=95)
                        image_bytes = buffer.getvalue()
                        image_b64 = base64.b64encode(image_bytes).decode('utf-8')
                        
                        processed_images.append({
                            "Index": i,
                            "Data": image_b64,
                            "Format": "image/jpeg",
                            "Timestamp": frame.timestamp.isoformat() if hasattr(frame, 'timestamp') and frame.timestamp else None
                        })
                        
                        logger.info(f"Successfully processed frame {i+1} (PIL Image)")
                    
                    # Unknown return type - try to debug
                    else:
                        logger.error(f"Unknown return type from get_image(): {type(image_data)}")
                        logger.error(f"Available methods: {[method for method in dir(image_data) if not method.startswith('_')]}")
                        
                        # Try to see if it has image data we can use
                        if hasattr(image_data, 'getvalue'):
                            logger.info("Trying getvalue() method")
                            try:
                                image_bytes = image_data.getvalue()
                                image_b64 = base64.b64encode(image_bytes).decode('utf-8')
                                processed_images.append({
                                    "Index": i,
                                    "Data": image_b64,
                                    "Format": "image/jpeg",
                                    "Timestamp": frame.timestamp.isoformat() if hasattr(frame, 'timestamp') and frame.timestamp else None
                                })
                                logger.info(f"Successfully processed frame {i+1} (via getvalue)")
                            except Exception as getval_ex:
                                logger.error(f"getvalue() failed: {getval_ex}")
                                continue
                        else:
                            logger.error(f"Cannot process unknown image data type: {type(image_data)}")
                            continue
                
                except AttributeError as ae:
                    logger.error(f"Failed to get image from SphericalImage: {ae}")
                    logger.error(f"Available SphericalImage methods: {[method for method in dir(spherical_image) if not method.startswith('_')]}")
                    continue
                
                except Exception as img_ex:
                    logger.error(f"Error processing image data: {img_ex}")
                    logger.error(f"Image data type: {type(image_data) if 'image_data' in locals() else 'undefined'}")
                    continue
                
            except Exception as frame_ex:
                logger.error(f"Failed to process frame {i+1}: {frame_ex}")
                logger.error(f"Frame processing traceback: {traceback.format_exc()}")
                continue
        
        logger.info(f"Successfully processed {len(processed_images)} out of {len(temp)} frames")
        
        return jsonify({
            "Success": True,
            "Data": processed_images,
            "Message": f"Retrieved {len(processed_images)} images from recording {recording.directory}"
        })
        
    except Exception as e:
        logger.error(f"Failed to get images: {e}")
        logger.error(f"Full traceback: {traceback.format_exc()}")
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
            print("Network and PostgreSQL service tests passed")
            print("  Ready for database connections")
        else:
            print("Network OK but PostgreSQL service issues detected")
            print(f"  {service_msg}")
    else:
        print("Network connectivity issues detected")
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
            print(f"{package}: Available")
        except ImportError:
            missing_packages.append(install_name)
            print(f"{package}: Missing")
    
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
        try:
            with open(args.config, 'r') as f:
                config = json.load(f)
            if bridge.update_config(config):
                print("OK Configuration loaded successfully")
            else:
                print("ERROR Failed to load configuration, using defaults")
        except Exception as e:
            print(f"ERROR Failed to load configuration file: {e}")
    
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