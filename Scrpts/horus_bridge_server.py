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
from typing import List, Dict, Any
import itertools
from PIL import Image
import psycopg2

from horus_media import Client, Size
from horus_db import Frames, Recordings, Frame, Recording
from horus_camera import SphericalCamera
#from Connection_settings import connection_settings, external_data  # From working script

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
        
        # Database credentials from Connection_settings
        #self.settings = connection_settings(**external_data)

        logger.warning("Using fallback hardcoded credentials")
        self.db_config = {
            "host": "xx.x.xx.xxx",
            "port": "xxxx",
            "database": "HorusWebMoviePlayer",
            "user": "xxxxxxxxxxxx",
            "password": "xx$%xxxxxxxxx"
        }

        # Attempt to connect to Horus and database on initialization
        self.connect_horus()
        self.connect_database(
            host=self.db_config["host"],
            port=self.db_config["port"],
            database=self.db_config["database"],
            user=self.db_config["user"],
            password=self.db_config["password"]
        )
        
    def get_database_connection_string(self):
        """Generate database connection string like the working script"""
        db_params = [
            ("host", self.db_config["host"]),
            ("port", self.db_config["port"]),
            ("dbname", self.db_config["database"]),
            ("user", self.db_config["user"]),
            ("password", self.db_config["password"]),
        ]
        return " ".join(map("=".join, filter(lambda x: x[1] is not None, db_params)))
    
    def connect_horus(self) -> bool:
        """Connect to Horus media server"""
        try:
            self.client = Client("http://10.0.10.100:5050/web/", timeout=20)
            self.client.attempts = 5
            self.is_connected = True
            logger.info("Horus connection: SUCCESS")
            return True
            
        except Exception as e:
            logger.error(f"Failed to connect to Horus: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.is_connected = False
            return False
    
    def connect_database(self, host: str, port: str, database: str, user: str, password: str) -> bool:
        """Connect to PostgreSQL database"""
        try:
            self.db_connection = psycopg2.connect(self.get_database_connection_string())
            
            # Test connection
            cursor = self.db_connection.cursor()
            cursor.execute("SELECT 1")
            cursor.close()
            
            logger.info(f"Database connection: SUCCESS (host={host}, port={port}, database={database})")
            return True
            
        except Exception as e:
            logger.error(f"Failed to connect to database: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            self.db_connection = None
            return False
    
    def get_recordings(self) -> List[Dict]:
        """Get list of available recordings"""
        try:
            if self.db_connection:
                recordings_manager = Recordings(self.db_connection)
                query_results = Recording.query(recordings_manager)
                
                recordings = []
                for recording in query_results:
                    print(f"Recording: {recording}")  # Debugging
                    recordings.append({
                        "id": recording.id,
                        "endpoint": recording.directory,
                        "boundingbox": str(recording.boundingbox) if recording.boundingbox else None
                    })
                
                return recordings
                
            else:
                logger.warning("No database connection for retrieving recordings")
                return []
                
        except Exception as e:
            logger.error(f"Failed to get recordings: {e}")
            logger.error(f"Traceback: {traceback.format_exc()}")
            return []
    
    def get_images(self, recording_endpoint: str, count: int = 5, width: int = 600, height: int = 600) -> List[Dict]:
        """Get images from a recording"""
        try:
            if not self.client or not self.is_connected or not self.db_connection:
                raise Exception("Not connected to Horus server or database")
            
            recordings_manager = Recordings(self.db_connection)
            recording = next(Recording.query(recordings_manager, directory_like=recording_endpoint), None)
            if not recording:
                raise Exception(f"No recording found for endpoint: {recording_endpoint}")
            
            recordings_manager.get_setup(recording)
            
            frames_manager = Frames(self.db_connection)
            frame_query = Frame.query(frames_manager, recordingid=recording.id, order_by="index")
            frames_list = list(itertools.islice(frame_query, count))
            
            sp_camera = SphericalCamera()
            sp_camera.set_network_client(self.client)
            sp_camera.set_horizontal_fov(90)
            sp_camera.set_yaw(0)
            sp_camera.set_pitch(-30)
            
            processed_images = []
            for i, frame in enumerate(frames_list):
                communication = sp_camera.request(frame, Size(width, height))
                image = communication.image
                
                buffer = io.BytesIO()
                image.save(buffer, format="JPEG")
                image_bytes = buffer.getvalue()
                image_b64 = base64.b64encode(image_bytes).decode('utf-8')
                
                processed_images.append({
                    "index": i,
                    "data": image_b64,
                    "format": "image/jpeg",
                    "timestamp": frame.timestamp.isoformat() if frame.timestamp else None
                })
            
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
                "data": image_b64,
                "format": "image/jpeg",
                "timestamp": selected_frame.timestamp.isoformat() if selected_frame.timestamp else None
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
    """Connect to Horus server and database"""
    try:
        data = request.json
        
        # Connect to Horus
        horus_success = bridge.connect_horus()
        
        # Connect to database
        db_success = False
        if 'database' in data:
            db_config = data['database']
            db_success = bridge.connect_database(
                host=db_config.get('host', bridge.db_config['host']),
                port=db_config.get('port', bridge.db_config['port']),
                database=db_config.get('database', bridge.db_config['database']),
                user=db_config.get('user', bridge.db_config['user']),
                password=db_config.get('password', bridge.db_config['password'])
            )
        else:
            db_success = bridge.connect_database(
                host=bridge.db_config['host'],
                port=bridge.db_config['port'],
                database=bridge.db_config['database'],
                user=bridge.db_config['user'],
                password=bridge.db_config['password']
            )
        
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
            "success": True,
            "data": recordings,
            "count": len(recordings)
        })
        
    except Exception as e:
        logger.error(f"Failed to get recordings: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/images', methods=['POST'])
def get_images():
    """Get images from a recording"""
    try:
        data = request.json
        
        recording_endpoint = data.get('recording_endpoint', 'Rotterdam360\\Ladybug5plus')
        count = data.get('count', 5)
        width = data.get('width', 600)
        height = data.get('height', 600)
        
        images = bridge.get_images(recording_endpoint, count, width, height)
        
        return jsonify({
            "success": True,
            "data": images,
            "count": len(images)
        })
        
    except Exception as e:
        logger.error(f"Failed to get images: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/image/<path:recording_endpoint>/<timestamp>', methods=['GET'])
def get_image_by_timestamp(recording_endpoint: str, timestamp: str):
    """Get specific image by timestamp"""
    try:
        width = request.args.get('width', 600, type=int)
        height = request.args.get('height', 600, type=int)
        
        image_data = bridge.get_image_by_timestamp(recording_endpoint, timestamp, width, height)
        
        return jsonify({
            "success": True,
            "data": image_data
        })
        
    except Exception as e:
        logger.error(f"Failed to get image by timestamp: {e}")
        logger.error(f"Traceback: {traceback.format_exc()}")
        return jsonify({
            "success": False,
            "error": str(e)
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

if __name__ == '__main__':
    print("=" * 60)
    print("HORUS MEDIA BRIDGE SERVER")
    print("=" * 60)
    print("Starting HTTP bridge server on http://localhost:5001")
    print("Endpoints available:")
    print("  GET  /health                 - Health check")
    print("  POST /connect                - Connect to services")
    print("  GET  /recordings             - Get recordings list")
    print("  POST /images                 - Get images from recording")
    print("  GET  /image/<endpoint>/<time> - Get image by timestamp")
    print("  POST /disconnect             - Disconnect from services")
    print("=" * 60)
    
    app.run(
        host='localhost',
        port=5001,
        debug=True,
        threaded=True
    )
