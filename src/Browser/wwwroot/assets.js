const database = () => new Promise((resolve, reject) => {
  const request = indexedDB.open('CardStudioAssets', 1);
  request.onupgradeneeded = () => request.result.createObjectStore('images', { keyPath: 'Id' });
  request.onsuccess = () => resolve(request.result);
  request.onerror = () => reject(request.error);
});
globalThis.cardStudioAssets = {
  async load() {
    const db = await database();
    try {
      return await new Promise((resolve, reject) => {
        const request = db.transaction('images').objectStore('images').getAll();
        request.onsuccess = () => resolve(JSON.stringify(request.result));
        request.onerror = () => reject(request.error);
      });
    } finally { db.close(); }
  },
  async save(json) {
    const images = JSON.parse(json);
    const db = await database();
    try {
      await new Promise((resolve, reject) => {
        const transaction = db.transaction('images', 'readwrite');
        const store = transaction.objectStore('images');
        store.clear();
        images.forEach(image => store.put(image));
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error);
      });
    } finally { db.close(); }
  }
};
