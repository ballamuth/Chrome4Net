#Google Chrome Native Messaging Extension Examples

##Echo Extension

###Synopsis

This extension receives messages sent from the page and sends them back.

###Installation
* Download the example and build it.
* Run Echo.exe with command *register*. This will generate native messaging host manifest and put a reference to it into the system registry.
* Start Google Chrome and open there the extensions page : chrome://extensions .
* Turn on the *Developer mode*, press *Load unpacked extension* button, navigate to the folder containing the extension
  (Echo/bin/Debug/extension or Echo/bin/Release/extension depending on the solution configuration) and install the extension.
* Open test.html in the browser (Echo/bin/Debug/extension/test.html or Echo/bin/Release/test.html depending on the solution configuration again).
* Leave *JSON Request* text area as it is and press *Send request* button in the test page. 
  The page will make a proper request for you : fill source and destination properties of the request.
* The reply should appear, containing source and destination properies swapped and the original request in the request property.
