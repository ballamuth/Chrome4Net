/*
 * Background page script for Chrome4Net Twain Scan example extension
 *
 * Konstantin Kuzvesov, 2015
 *
 */

chrome.runtime.onConnect.addListener( function(port) {
	
	var manifest = chrome.runtime.getManifest();
	port.port = chrome.runtime.connectNative( manifest.name.toLowerCase() );
	port.port.port = port;

	port.onMessage.addListener( function(message, sender) {
		return sender.port.postMessage(message); 
	} );

	port.port.onMessage.addListener( function(message, sender) {
		if (message.action && (message.action==="transfer_image")) {
			return processImageTransfer(message, sender);
		}
		return sender.port.postMessage(message);
	} );
	
	port.onDisconnect.addListener( function(sender) {
		sender.port.disconnect();
	} );

} );

function processImageTransfer(message, port)
{
	if (message.ok) {
		if (message.chunk.start === 0)
		{
			port.image = {};
			port.image.chunks = [];
		}
		port.image.chunks.push( message.chunk.data );
		if ((message.chunk.start + message.chunk.length) === message.image.size)
		{
			message.image.data = port.image.chunks.join("");
			delete message.chunk;
			delete port.image;
			port.port.postMessage( message );
		}
	} else {
		port.port.postMessage(message);
	}
}


/* eof */