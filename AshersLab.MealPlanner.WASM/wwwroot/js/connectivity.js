// Tells the sync loop the moment the browser thinks it is back online, so queued edits go without waiting for
// the next retry. A hint only: "online" can fire with no route to the server, and the loop retries anyway.
export function onOnline(dotnet) {
  addEventListener('online', () => dotnet.invokeMethodAsync('OnOnline'));
}
