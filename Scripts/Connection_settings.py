from pydantic import BaseModel



class connection_settings(BaseModel):
    host: str
    port : str
    dbname: str
    dbuser: str
    password: str




external_data = {
    "host": "10.0.10.100",
    "port": str(5432),
    "dbname": "HorusWebMoviePlayer",
    "dbuser": "pocmsro",
    "password": "ZSE$%67ujm",
}
