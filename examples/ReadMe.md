#Google Chrome Native Messaging Extension Examples

##Echo Extension and Relayed Echo Extension

###Synopsis

These extensions receive messages sent from the page and send them back to the page.

The difference is that the Echo extension runs in a single process, while Relayed Echo extension starts one process to interface with Google Chrome
and the second process to process native messages. This two-step messages processing mechanism appears the only solution if you use some
third-party libraries that dare to write their own messages to standard error or, which is worse, to standard output.

###Installation
* Download the example and build it.
* Run Echo.exe with command *register*. This will generate a native messaging host manifest and put a reference to the manifest into the system registry.
* Start Google Chrome and open there the extensions page : chrome://extensions .
* Turn on the *Developer mode*, press *Load unpacked extension* button, navigate to the folder containing the extension
  (Echo/bin/Debug/extension or Echo/bin/Release/extension depending on the solution configuration) and install the extension.
* Open test.html in the browser (Echo/bin/Debug/test.html or Echo/bin/Release/test.html depending on the solution configuration again).
* Leave *JSON Request* text area as it is and press *Send request* button in the test page. 
  The page will make a proper request for you : fill source and destination properties of the request.
* The reply should appear, containing source and destination properies swapped and the original request in the request property.

##Twain Scan Extension

###Synopsis

This extension interfaces with TWAIN drivers using requests sent from the page.

###Installation
* Download the example and build it.
* Run TwainScan.exe with command *register*. This will generate a native messaging host manifest and put a reference to the manifest into the system registry.
* Start Google Chrome and open there the extensions page : chrome://extensions .
* Turn on the *Developer mode*, press *Load unpacked extension* button, navigate to the folder containing the extension
  (TwainScan/bin/Debug/extension or TwainScan/bin/Release/extension depending on the solution configuration) and install the extension.
* Open test.html in the browser (TwainScan/bin/Debug/examples/test.html or TwainScan/bin/Release/examples/test.html again depending on the solution configuration).
* Leave *JSON Request* text area as it is and press *Send request* button in the test page. 
  The page will make a proper request for you : fill source and destination properties of the request.
* The reply should appear, containing source and destination properies swapped, the original request, exception and comment texts, and ok flag set to false.

###Testing
* Send sequentially get version and list actions requests by pressing corresponding buttons. 
* Then try to acquire sample image and transfer it to the page. The image with text 'sample' should appear.
* Then try to initialize twain interface, list twain sources, select twain sourcse, scan the image by pressing acquire image 
  and then transfer the image to the page.

Konstantin Kuzvesov, 2015