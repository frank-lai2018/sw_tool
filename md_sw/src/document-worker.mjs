import { buildDocument } from './document-model.mjs';
self.onmessage = event => {
  try { self.postMessage({ model: buildDocument(event.data) }); }
  catch (error) { self.postMessage({ error: error.message }); }
};
