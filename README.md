# Bulkexport project
Real time files pickup and send to a remote server demonstration. The project is obsolete at the moment of publication.

# Installation Design
The installation process is as follows:

The BulkExport service starts on every system boot.

During its startup, the service mounts the network drive U: if the HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters\Mapping registry key exists.

If errors occur when transferring zip archives over the network, disk U: is reconnected.

Please note that the service won’t start if the source folder doesn’t exist.

# Application Design
Note: BulkExport runs as a SYSTEM user application.

# File System Watcher
The File System Watcher waits for some application to generate a file in source_dir, the directory where the SVDMP files are placed. When a file is created, a Create event is generated, and the File System Watcher sends the file name to the Reader’s queues according to their current load. 
If the file is currently open, there is no access to it and the application can write to it at random times. When the file is either closed or opened no events are generated. Therefore, the only way to take control of a file is by repeating attempts to open it. This is handled by the Reader.

# The Reader
The Reader has 3 queues for storing file names, which will be denoted as ReaderQueue#1, ReaderQueue#2 and ReaderQueue#3 with maximum capacity 100 file names each. Queues # 2 and # 3 have a dual purpose: preventing delays and parallel processing in the event of a mass arrival of files. It operates as follows:

If the size of ReaderQueue#1 is less than its maximum capacity, the file name is enqueued. Otherwise it is moved to ReaderQueue#2. The Reader waits 6ms before trying to read the file. If it doesn’t succeed in opening the file, it is moved to ReaderQueue#2. 
A similar process happens in the other queues, with their respective maximum capacity.
However, If the file cannot be opened in ReaderQueue#3, the file name is dropped and there will be no more attempts to read it anymore.
Usually, this process happens fast enough that it will look like there are 0 files in the queues.

In any case, if the file was opened, it is stored in the memory and moved to the Compressor.   

# The Compressor
## Normal mode
The Compressor has a queue for storing the files while waiting for compression, which is named Compress. It has a maximum capacity of 100 files. The files arrive as memory streams. If the file has an extension .svdmp checks for the absence of the SvError signature in the last 500 bytes of the stream. If no error is detected or the file is not a .svdmp, it is compressed. The file with an error is sent to the NetMover queue. The compression level is determined by the value of the HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters\CompressionLevel registry key.

If more files than the maximum limit (which is specified by the user when starting the server using the flag /zip) has been compressed, or that the total size of the compressed files are more than 100MB, the compressor finishes the zip archive and sends it to the Network Mover.

## UIC mode
The compressor input reads the file extension and switches to the file pair generation mode. If the input file has the extension .bmp then expects the arrival of a file with the same name and the .xml extension. When the file arrives .the xml file is then expected to be .bmp. When a pair of files is formed, the image file is cropped. During the cropping process, the component dimensions, rotation angle, and resolution are read from the xml file. The cropping area expands by 50% and rotates in the opposite direction. Then the dimensions of the rotated area are determined, that is, the coordinates of the circumscribed rectangle with sides parallel to the sides of the image, and this region is extracted. The cut image and the xml file are sent for compression.

When it is impossible to determine the cropping parameters, the files are considered erroneous and are sent to the errors folder instead of being packed. The following is a list of the parameters used:

PelSizeX, PelSizeY - the image resolution (px/mm), if not found resolution of 59.24px/mm is assumed. 

PhysOrient - physical orientation of the image (deg). Some angle, a multiple of 90 degrees.

XComponentSize, YComponentSize - component dimensions in mm. If not found XBodySize and YBodySize are used instead. 

XExpected, YExpected - expected position of the component’s center.

ThetaExpected - expected rotation of the component (mdeg).

XFound, YFound - observed position of the center of the component.

AngleFound - observed rotation of the component (mdeg).

ResultCode - contains CV status. In particular PCVIS_SUCCESS if the component was identified successfully.

HandednessFlip - rotation direction switch.

# The Net Mover
The Net Mover has a queue for storing zipped svdmps before they are moved to the U: drive. It is named netQ and has a maximum capacity of 3 items (not more than 300Mb in total). 
The Net Mover checks if the U: drive, which was initialized during the installation process, is available. If it is, zips from netQ are enqueued and moved to the U: drive. Otherwise, an attempt is made to remount the U: drive. In case of failure, the zip file is dropped. The process diagram is shown in Fig. 1

Open diagramm.png

Fig.1 Scheme for processing incoming dumps.

# The cleaner
The cleaner sequentially removes all original files.

# Debugging Tools
There are two types of errors and messages of the service: Low Priority and High Priority. The following tools are great in debugging and finding these errors:

- Windows Event Viewer: Only High Priority issues shall appear here, first and foremost issues regarding the connection to the network directory, such as stoppage of zips creation.
In order to use it, open an EventViewer, go to Windows Logs → Applications.

- Windows Services App: Can be useful in order to find whether the service is currently running or not.

error.log file: Inside the Install subfolder of the BulkExport zip, an error.log file will be created once the service is started. Any issues regarding the installation process appear here.

- BulkExport Diagnostics Messages: A new feature (since version 1.1.6) is a local telnet connection which prints both Low Priority and High Priority messages, alongside with the current RAM, CPU consumption and queue states for each of the queues. 
In order to connect it, please use a TCP client (such as PuTTY). Connect to localhost at port 1212.
It is important to note that the messages are live, meaning that you will not see any messages for events that happened before your connection. 
The diagnostics messages window has a messages queue inside, which receives diagnostics messages from different parallel threads. If there is a connection, messages are sent to it. 

- Windows Performance Monitors: These tools are great for monitoring CPU, RAM and in fact any diagnostics in your PC. Simply activate a Data Collector Set and you can check for different processes in it, such as the BulkExport. Usually Create from a template -> System Diagnostics would be good for the service. Note that by default, the Performance Monitor snapshots only 1 minute. You can change that by accessing “stop condition” in the properties menu.
