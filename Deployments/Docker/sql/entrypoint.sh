#!/bin/bash
# entrypoint.sh

# Start SQL Server in the background
/opt/mssql/bin/sqlservr &

# Wait for SQL Server to start
echo "Waiting for SQL Server to start..."
# Loop until sqlcmd can successfully connect
# -C trusts the cert, -Q "SELECT 1" is a simple ping
for i in {1..60};
do
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1
    if [ $? -eq 0 ]; then
        echo "SQL Server is UP!"
        break
    else
        echo "SQL Server is still starting... ($i/60)"
        sleep 2
    fi
done

# Run initialization scripts
echo "Running database initialization..."
# -C flag to trust the server certificate (Required for mssql-tools18)
# -i meaning Input file - read SQL from a file
/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -i /init.sql

# Keep container running
wait