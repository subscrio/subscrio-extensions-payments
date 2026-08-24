import type { Pool } from 'pg';
import {
  PaymentFilterDtoSchema,
  type PaymentDto,
  type PaymentFilterDto,
  type PaymentListResult,
} from '../dtos/PaymentDto.js';
import { PaymentMapper, type PaymentRecord } from '../mappers/PaymentMapper.js';

export interface InsertPaymentInput {
  amountPaid: number;
  currency: string;
  customerId: number | null;
  subscriptionId: number | null;
  billingCycleId: number | null;
  externalProductId: string | null;
  durationValue: number | null;
  durationUnit: string | null;
  stripeInvoiceId: string;
  stripeEventId: string | null;
}

export class PostgresPaymentRepository {
  constructor(private readonly pool: Pool) {}

  async insert(input: InsertPaymentInput): Promise<void> {
    await this.pool.query(
      `INSERT INTO subscrio.payments (
        amount_paid, currency, customer_id, subscription_id, billing_cycle_id,
        external_product_id, duration_value, duration_unit,
        stripe_invoice_id, stripe_event_id
      ) VALUES (
        $1, $2, $3, $4, $5,
        $6, $7, $8,
        $9, $10
      )
      ON CONFLICT (stripe_invoice_id) DO NOTHING`,
      [
        input.amountPaid,
        input.currency,
        input.customerId,
        input.subscriptionId,
        input.billingCycleId,
        input.externalProductId,
        input.durationValue,
        input.durationUnit,
        input.stripeInvoiceId,
        input.stripeEventId,
      ]
    );
  }

  async get(id: number): Promise<PaymentDto | null> {
    const result = await this.pool.query<PaymentRecord>(
      `SELECT * FROM subscrio.payments WHERE id = $1`,
      [id]
    );
    const row = result.rows[0];
    return row ? PaymentMapper.toDto(row) : null;
  }

  async list(filters?: Partial<PaymentFilterDto>): Promise<PaymentListResult> {
    const parsed = PaymentFilterDtoSchema.safeParse(filters ?? {});
    if (!parsed.success) {
      throw new Error(`Invalid payment filters: ${parsed.error.message}`);
    }
    const f = parsed.data;

    const where: string[] = [];
    const params: unknown[] = [];
    let i = 1;

    const add = (clause: string, value: unknown) => {
      where.push(clause.replace('?', `$${i++}`));
      params.push(value);
    };

    if (f.customerId != null) add('customer_id = ?', f.customerId);
    if (f.subscriptionId != null) add('subscription_id = ?', f.subscriptionId);
    if (f.startDate) add('created_at >= ?', f.startDate instanceof Date ? f.startDate : new Date(f.startDate));
    if (f.endDate) add('created_at <= ?', f.endDate instanceof Date ? f.endDate : new Date(f.endDate));

    const whereSql = where.length > 0 ? `WHERE ${where.join(' AND ')}` : '';
    const sortOrder = f.sortOrder === 'asc' ? 'ASC' : 'DESC';

    const countResult = await this.pool.query<{ count: string }>(
      `SELECT COUNT(*)::text AS count FROM subscrio.payments ${whereSql}`,
      params
    );
    const total = Number(countResult.rows[0]?.count ?? 0);

    const dataParams = [...params, f.limit, f.offset];
    const dataResult = await this.pool.query<PaymentRecord>(
      `SELECT * FROM subscrio.payments
       ${whereSql}
       ORDER BY created_at ${sortOrder}, id ${sortOrder}
       LIMIT $${i++} OFFSET $${i}`,
      dataParams
    );

    return {
      data: dataResult.rows.map(PaymentMapper.toDto),
      total,
    };
  }
}
