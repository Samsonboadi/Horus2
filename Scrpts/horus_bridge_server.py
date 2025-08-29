# horus_bridge_server.py
"""
HTTP Bridge Server for Horus Media Client integration with C# ArcGIS Pro Add-in
This server provides a REST API that your C# application can call
"""

from flask import Flask, request, jsonify, send_file
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

from horus_media import Client, Size
from horus_db import Frames, Recordings, Frame, Recording
from horus_camera import SphericalCamera

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
            "host": "xx.x.xx.xxx",
            "port": "xxxx",
            "database": "HorusWebMoviePlayer",
            "user": "xxxxxxxxxxxx",
            "password": "xx$%xxxxxxxxx"
        }
        
        self.horus_config = {
            "url": "http://10.0.10.100:5050/web/"
        }

    def update_config(self, config_data):
        """Update configuration from external source (like C# application)"""
        try:
            if 'database' in config_data:
                self.db_config.update(config_data['database'])
                logger.info(f"Updated database config: host={self.db_config['host']}, database={self.db_config['database']}")
            
            if 'horus' in config_data:
                self.horus_config.update(config_data['horus'])
                logger.info(f"Updated Horus config: url={self.horus_config['url']}")
                
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
                    return self.update_config(config_data)
            else:
                logger.warning(f"Config file not found: {config_path}")
                return False
        except Exception as e:
            logger.error(f"Failed to load config from file: {e}")
            return False
        
    def get_database_connection_string(self):
        """Generate database connection string"""
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
        try:
            url = horus_url or self.horus_config["url"]
            self.client = Client(url, timeout=20)
            self.client.attempts = 5
            self.is_connected = True
            logger.info(f"Horus connection: SUCCESS - {url}")
            return True
            
        except Exception as e:
            logger.error(f"Failed to connect to Horus: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.is_connected = False
            return False
    
    def connect_database(self, db_config=None) -> bool:
        """Connect to PostgreSQL database"""
        try:
            if db_config:
                self.db_config.update(db_config)
            
            connection_string = self.get_database_connection_string()
            logger.info(f"Attempting database connection with: {connection_string.replace(self.db_config.get('password', ''), '***')}")
            
            self.db_connection = psycopg2.connect(connection_string)
            
            # Test connection
            cursor = self.db_connection.cursor()
            cursor.execute("SELECT 1")
            cursor.close()
            
            logger.info(f"Database connection: SUCCESS")
            return True
            
        except Exception as e:
            logger.error(f"Failed to connect to database: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.db_connection = None
            return False
    
    def get_recordings(self) -> List[Dict]:
        """Get list of available recordings"""
        try:
            if not self.db_connection:
                logger.warning("No database connection for retrieving recordings")
                return []

            recordings_manager = Recordings(self.db_connection)
            query_results = Recording.query(recordings_manager)
            
            recordings = []
            for recording in query_results:
                logger.info(f"Found recording: ID={recording.id}, Directory={recording.directory}")
                recordings.append({
                    "Id": str(recording.id),
                    "Endpoint": recording.directory,
                    "Name": recording.directory.split('\\')[-1] if recording.directory else f"Recording {recording.id}",
                    "Description": f"Recording from {recording.directory}",
                    "CreatedDate": recording.created.isoformat() if hasattr(recording, 'created') and recording.created else None
                })
            
            logger.info(f"Retrieved {len(recordings)} recordings")
            return recordings
                
        except Exception as e:
            logger.error(f"Failed to get recordings: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            return []
    
    def get_images(self, recording_endpoint: str, count: int = 5, width: int = 600, height: int = 600) -> List[Dict]:
        """Get images from a recording"""
        try:
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
    """Health check endpoint"""
    return jsonify({
        "status": "running",
        "timestamp": datetime.now().isoformat(),
        "horus_connected": bridge.is_connected,
        "database_connected": bridge.db_connection is not None
    })

@app.route('/connect', methods=['POST'])
def connect():
    """Connect to Horus server and database with provided configuration"""
    try:
        data = request.json
        logger.info(f"Received connection request with data keys: {data.keys() if data else 'No data'}")
        
        # Update configuration if provided
        if data:
            bridge.update_config(data)
        
        # Connect to Horus
        horus_url = None
        if 'horus' in data and 'url' in data['horus']:
            horus_url = data['horus']['url']
        
        horus_success = bridge.connect_horus(horus_url)
        
        # Connect to database
        db_config = None
        if 'database' in data:
            db_config = data['database']
        
        db_success = bridge.connect_database(db_config)
        
        return jsonify({
            "success": True,
            "horus_connected": horus_success,
            "database_connected": db_success,
            "message": "Connection attempt completed"
        })
        
    except Exception as e:
        logger.error(f"Connection failed: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e),
            "traceback": traceback.format_exc()
        }), 500

@app.route('/recordings', methods=['GET'])
def get_recordings():
    """Get list of available recordings"""
    try:
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
        if bridge.client:
            bridge.client = None
            bridge.is_connected = False
        
        if bridge.db_connection:
            bridge.db_connection.close()
            bridge.db_connection = None
        
        return jsonify({
            "success": True,
            "message": "Disconnected successfully"
        })
        
    except Exception as e:
        logger.error(f"Disconnect failed: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/config', methods=['POST'])
def update_config():
    """Update bridge configuration"""
    try:
        data = request.json
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

def parse_arguments():
    """Parse command line arguments"""
    parser = argparse.ArgumentParser(description='Horus Media Bridge Server')
    parser.add_argument('--config', help='Path to configuration JSON file')
    parser.add_argument('--port', type=int, default=5001, help='Server port (default: 5001)')
    parser.add_argument('--host', default='localhost', help='Server host (default: localhost)')
    return parser.parse_args()

if __name__ == '__main__':
    args = parse_arguments()
    
    print("=" * 60)
    print("HORUS MEDIA BRIDGE SERVER")
    print("=" * 60)
    print(f"Starting HTTP bridge server on http://{args.host}:{args.port}")
    
    # Load configuration from file if provided
    if args.config:
        print(f"Loading configuration from: {args.config}")
        if bridge.load_config_from_file(args.config):
            print("Configuration loaded successfully")
        else:
            print("Failed to load configuration, using defaults")
    
    print("Endpoints available:")
    print("  GET  /health                 - Health check")
    print("  POST /connect                - Connect to services")
    print("  POST /config                 - Update configuration")
    print("  GET  /recordings             - Get recordings list")
    print("  POST /images                 - Get images from recording")
    print("  GET  /image/<endpoint>/<time> - Get image by timestamp")
    print("  POST /disconnect             - Disconnect from services")
    print("=" * 60)
    
    app.run(
        host=args.host,
        port=args.port,
        debug=True,
        threaded=True
    )
