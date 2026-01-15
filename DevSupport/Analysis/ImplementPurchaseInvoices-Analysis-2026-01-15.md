# Implement Purchase Invoices (Facturi de Achiziție) - Analysis & Plan
**Date:** 2026-01-15  
**Task:** ImplementPurchaseInvoices  
**Status:** Analysis Phase  

## Overview
Implement purchase invoices functionality requiring 5 new tables: DocumentState, Document, Invoice, DocumentDetail, InvoiceDetail, and Stock (if not exists). Follow ValyanERP Vertical Slices Architecture and database conventions.

## Current System Analysis
- **Existing Tables:** Persoane, Users, Partners, TipuriDocumente, Articole (Items), Locations (from SocietateaProprie)
- **Stock Table:** Does not exist - need to create for inventory management
- **Document Types:** TipuriDocumente table exists with TipDocumentCode (FFA for purchase invoices)
- **Conventions:** UNIQUEIDENTIFIER Id, audit columns (IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy), stored procedures for all operations

## Requirements Breakdown
- **DocumentState:** 3 states (draft, valid, canceled)
- **Document:** Header info with dates, numbers, state, type (FFA)
- **Invoice:** Linked to Document, partner, amounts
- **DocumentDetail:** Line items (items, quantities)
- **InvoiceDetail:** Value details (prices, VAT)
- **Stock:** Update quantities for stockable items

## Implementation Plan

### Step 1: Create SQL Migration Scripts for New Tables
Create the following SQL migration scripts in `Database/Scripts/` following ValyanERP conventions:
- **042_DocumentState.sql**: Create DocumentState table with UNIQUEIDENTIFIER Id, Name, Code, IsActive, audit columns. Insert 3 seed records (Draft, Valid, Canceled).
- **043_Document.sql**: Create Document table with FK to DocumentState, Users (CreatedBy/UpdatedBy), TipuriDocumente. Include document number, date, state, type.
- **044_Invoice.sql**: Create Invoice table with FK to Document, Partners. Include invoice-specific fields like due date, total amounts.
- **045_DocumentDetail.sql**: Create DocumentDetail table with FK to Document, Articole. Include quantity, unit price.
- **046_InvoiceDetail.sql**: Create InvoiceDetail table with FK to Invoice, DocumentDetail. Include VAT rates, amounts.
- **047_Stock.sql**: Create Stock table with FK to Articole, Locations. Include quantity on hand, reserved quantity.
- **048_AddConstraintsAndIndexes.sql**: Add foreign key constraints, indexes on frequently queried columns (e.g., Document.StateId, Stock.ItemId).

### Step 2: Create Stored Procedures for Inserting Purchase Invoices
Create stored procedures in `Database/Scripts/049_StoredProcedures_PurchaseInvoices.sql`:
- **sp_DocumentState_GetAll**: Retrieve all active document states.
- **sp_Document_InsertPurchaseInvoice**: Master stored procedure that inserts a complete purchase invoice transactionally:
  - Insert into Document table
  - Insert into Invoice table
  - Insert into DocumentDetail and InvoiceDetail tables
  - Update Stock table for stockable items (if invoice state is Valid)
  - Use TRY-CATCH for error handling and rollback on failure
- **sp_Stock_UpdateQuantity**: Update stock quantities when invoice state changes.
- **Supporting SPs**: Create GetById, GetAll, Update, Delete for Document, Invoice, DocumentDetail, InvoiceDetail, Stock tables.

### Step 3: Test the Implementation
- **Unit Tests**: Create SQL scripts to test each stored procedure individually with sample data.
- **Integration Test**: Execute sp_Document_InsertPurchaseInvoice with complete purchase invoice data and verify all tables are populated correctly.
- **Stock Update Test**: Test that stock quantities are updated only when invoice is validated (state = Valid).
- **Error Handling Test**: Test rollback behavior when inserting invalid data (e.g., non-existent partner).
- **Performance Test**: Insert multiple invoices and verify query performance with indexes.

## Dependencies
- Partners table (existing)
- Articole table (existing)
- Locations table (existing)
- TipuriDocumente table (existing, FFA code)

## Risk Assessment
- **High:** Stock table creation - ensure no conflicts with existing inventory logic
- **Medium:** Master SP complexity - handle transactions properly
- **Low:** State management - simple enum-like table

## Success Criteria
- All tables created with proper constraints
- Stored procedures execute without errors
- Sample purchase invoice can be inserted
- Stock quantities update correctly
- Data integrity maintained

## Next Steps
Execute plan steps in order, marking each as ✅ DONE upon completion.