# ABX Exchange Client

A C# client application that connects to the ABX exchange server to retrieve stock ticker data via TCP protocol.

## Prerequisites

- .NET 6.0 or higher
- Node.js 16.17.0 or higher (for running the ABX server)

## Setup Instructions

### 1. Clone the Repository

```bash
git clone https://github.com/krisbiradar/.NetABXExchangeClient
cd ABXExchangeClient
```

### 2. Setup the ABX Exchange Server

1. Download and extract the `abx_exchange_server.zip` file from the assignment
2. Navigate to the extracted folder
3. Run the server:
   ```bash
   node main.js
   ```
4. The server should start on port 3000

### 3. Run the C# Client

```bash
dotnet run
```

That's it! The client will automatically build and run.

## How It Works

The client performs the following operations:

1. **Initial Request**: Sends a "Stream All Packets" request (Call Type 1) to get all available data
2. **Parse Response**: Parses the binary response data into packet objects
3. **Detect Missing Sequences**: Identifies any missing sequence numbers in the received data
4. **Request Missing Packets**: Uses "Resend Packet" requests (Call Type 2) to retrieve any missing packets
5. **Generate Output**: Creates a JSON file with all packets sorted by sequence number

## Binary Protocol Details

### Request Format
- **Call Type 1** (Stream All): `[0x01, 0x00]`
- **Call Type 2** (Resend): `[0x02, sequence_number]`

### Response Format (17 bytes per packet)
- Symbol: 4 bytes (ASCII, big-endian)
- Buy/Sell Indicator: 1 byte (ASCII: 'B' or 'S')
- Quantity: 4 bytes (int32, big-endian)
- Price: 4 bytes (int32, big-endian)
- Packet Sequence: 4 bytes (int32, big-endian)

## Output

The client generates a JSON file named `abx_data_YYYYMMDD_HHMMSS.json` containing an array of packet objects:

```json
[
  {
    "Symbol": "MSFT",
    "BuySellIndicator": "B",
    "Quantity": 100,
    "Price": 25000,
    "PacketSequence": 1
  },
  
]
```

## Error Handling

The client includes robust error handling for:
- Network connection issues
- Malformed packets
- Missing sequence recovery
- Server disconnections

## Project Structure

```
ABXExchangeClient/
├── ConsoleApp8.csproj
├── Program.cs
├── README.md
└── abx_data_*.json (generated output files)
```
