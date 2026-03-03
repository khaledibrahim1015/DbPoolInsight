import http from 'k6/http';
import { sleep, check } from 'k6';

// Use container name, not localhost — they're on the same Docker network
const BASE_URL = 'http://efcore-api:8080';

export const options = {
    scenarios: {

        // Phase 1: Warm up — sequential, builds reuse ratio
        warmup: {
            executor: 'constant-vus',
            vus: 1,
            duration: '30s',
            startTime: '0s',
            tags: { phase: 'warmup' },
        },

        // Phase 2: Concurrent — expands physical pool instances
        concurrent: {
            executor: 'ramping-vus',
            startVUs: 0,
            stages: [
                { duration: '20s', target: 30 },
                { duration: '30s', target: 30 },
                { duration: '10s', target: 0 },
            ],
            startTime: '35s',
            tags: { phase: 'concurrent' },
        },

        // Phase 3: Sustained high load — holds context 2s, stresses pool
        sustained_high: {
            executor: 'constant-vus',
            vus: 50,
            duration: '60s',
            startTime: '100s',
            tags: { phase: 'sustained_high' },
        },

        // Phase 4: Overflow — push past pool size 128
        overflow: {
            executor: 'constant-vus',
            vus: 150,
            duration: '30s',
            startTime: '170s',
            tags: { phase: 'overflow' },
        },
    },

    thresholds: {
        http_req_duration: ['p(95)<5000'],
        http_req_failed: ['rate<0.1'],
    },
};

export default function () {
    const scenario = __ENV.K6_SCENARIO_NAME;

    if (scenario === 'warmup') {
        const res = http.post(`${BASE_URL}/api/pool/test/sequential?requests=10&delayMs=50`);
        check(res, { 'warmup ok': r => r.status === 200 });

    } else if (scenario === 'concurrent') {
        const res = http.post(`${BASE_URL}/api/pool/test/concurrent?parallelRequests=10&delayMs=200`);
        check(res, { 'concurrent ok': r => r.status === 200 });

    } else if (scenario === 'sustained_high') {
        const res = http.get(`${BASE_URL}/api/pooldiagnostics/sustained-high-load?waves=1&requestsPerWave=10&delayMs=100`);
        check(res, { 'sustained ok': r => r.status === 200 });

    }
    else if (scenario === 'overflow') {
        const res = http.get(`${BASE_URL}/api/pooldiagnostics/sustained-high-load?waves=1&requestsPerWave=150&delayMs=50`);
        check(res, { 'overflow ok': r => r.status === 200 });
    }

    sleep(0.5);
}