import { z } from 'zod';

export interface PaymentDto {
  id: number;
  createdAt: string;
  amountPaid: number;
  currency: string;
  customerId?: number | null;
  subscriptionId?: number | null;
  billingCycleId?: number | null;
  externalProductId?: string | null;
  durationValue?: number | null;
  durationUnit?: string | null;
  stripeInvoiceId: string;
  stripeEventId?: string | null;
}

export const PaymentFilterDtoSchema = z.object({
  customerId: z.number().int().positive().optional(),
  subscriptionId: z.number().int().positive().optional(),
  startDate: z.union([z.string(), z.date()]).optional(),
  endDate: z.union([z.string(), z.date()]).optional(),
  sortOrder: z.enum(['asc', 'desc']).optional().default('desc'),
  limit: z.number().int().min(1).max(500).optional().default(50),
  offset: z.number().int().min(0).optional().default(0),
});

export type PaymentFilterDto = z.infer<typeof PaymentFilterDtoSchema>;

export interface PaymentListResult {
  data: PaymentDto[];
  total: number;
}
