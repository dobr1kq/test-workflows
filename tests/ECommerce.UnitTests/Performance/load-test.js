import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

/**
 * Two independent scenarios:
 *   1. load_products   — навантажувальне тестування списку товарів
 *   2. checkout_race   — стрес-тестування одночасного оформлення замовлень
 *                        (стан гонки за залишки на складі)
 */
export const options = {
    scenarios: {
        load_products: {
            executor: 'ramping-vus',
            stages: [
                { duration: '30s', target: 20 },
                { duration: '1m',  target: 20 },
                { duration: '10s', target: 0  },
            ],
            exec: 'loadTestProducts',
        },
        checkout_race: {
            executor: 'constant-vus',
            vus: 50,
            duration: '30s',
            startTime: '2m',        // starts after the load test completes
            exec: 'stressTestCheckout',
        },
    },
    thresholds: {
        'http_req_duration{scenario:load_products}': ['p(95)<500'],
        'http_req_failed{scenario:load_products}':   ['rate<0.01'],
    },
};

/**
 * setup() runs once before all VUs start.
 * Creates one limited-stock product that every VU will compete for.
 */
export function setup() {
    const sku = `RACE-${Date.now()}`;
    const res = http.post(
        `${BASE_URL}/api/products`,
        JSON.stringify({
            name:          'Race Condition Product',
            description:   'Limited stock — stress test target',
            price:         49.99,
            stockQuantity: 30,        // only 30 units; 50 VUs will compete
            category:      'Test',
            sku:           sku,
        }),
        { headers: { 'Content-Type': 'application/json' } }
    );

    if (res.status === 201) {
        const body = JSON.parse(res.body);
        return { raceProductId: body.id };
    }
    console.warn(`setup: failed to create race product (${res.status}): ${res.body}`);
    return { raceProductId: null };
}

// ── Scenario 1: load test for product listing ─────────────────────────────────
export function loadTestProducts() {
    const res = http.get(`${BASE_URL}/api/products`);
    check(res, {
        'GET /api/products → 200': (r) => r.status === 200,
        'response has products':   (r) => JSON.parse(r.body).length > 0,
    });
    sleep(1);
}

// ── Scenario 2: concurrent checkout — race condition for limited stock ─────────
export function stressTestCheckout(data) {
    if (!data || !data.raceProductId) return;

    // Each VU+iteration uses a unique user to simulate real concurrent shoppers
    const userId  = `race-user-${__VU}-${__ITER}`;
    const headers = { 'X-User-Id': userId, 'Content-Type': 'application/json' };

    // Step 1: try to add the limited-stock product to cart
    const addRes = http.post(
        `${BASE_URL}/api/cart/items`,
        JSON.stringify({ productId: data.raceProductId, quantity: 1 }),
        { headers }
    );

    const addedOk = check(addRes, {
        'add item: success or stock exhausted': (r) => r.status === 200 || r.status === 400,
    });

    if (!addedOk || addRes.status !== 200) {
        sleep(0.5);
        return;
    }

    // Step 2: immediately checkout — multiple VUs hit this concurrently,
    //         only the first 30 should succeed (stock = 30); the rest get 400.
    const checkoutRes = http.post(
        `${BASE_URL}/api/cart/checkout`,
        null,
        { headers }
    );

    check(checkoutRes, {
        'checkout: success (stock available)':   (r) => r.status === 200,
        'checkout: rejected (stock exhausted)':  (r) => r.status === 400,
        'checkout: one of expected statuses':    (r) => r.status === 200 || r.status === 400,
    });

    sleep(0.5);
}
