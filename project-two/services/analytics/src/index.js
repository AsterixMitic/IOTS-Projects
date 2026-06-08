const http = require('http');
const { URL } = require('url');

const serviceName = process.env.SERVICE_NAME || 'analytics';
const brokerMode = (process.env.BROKER_MODE || 'mqtt').toLowerCase();
const port = Number(process.env.PORT || 3001);

const config = {
  serviceName,
  brokerMode,
  windowSeconds: Number(process.env.WINDOW_SECONDS || 10),
  alertThreshold: Number(process.env.ALERT_THRESHOLD || 50),
};

function sendJson(res, statusCode, payload) {
  const body = JSON.stringify(payload);
  res.writeHead(statusCode, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(body),
  });
  res.end(body);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);

  if (req.method === 'GET' && url.pathname === '/health') {
    return sendJson(res, 200, { status: 'ok', service: serviceName, brokerMode });
  }

  if (req.method === 'GET' && url.pathname === '/config') {
    return sendJson(res, 200, config);
  }

  if (req.method === 'POST' && url.pathname === '/window') {
    return sendJson(res, 202, {
      accepted: true,
      message: 'Windowing logic will be wired in the next step.',
      brokerMode,
    });
  }

  return sendJson(res, 404, { error: 'Not found' });
});

server.listen(port, '0.0.0.0', () => {
  console.log(`${serviceName} service listening on ${port} in ${brokerMode} mode`);
});
