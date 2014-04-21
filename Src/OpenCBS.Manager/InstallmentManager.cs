﻿// Octopus MFS is an integrated suite for managing a Micro Finance Institution: 
// clients, contracts, accounting, reporting and risk
// Copyright © 2006,2007 OCTO Technology & OXUS Development Network
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
//
// Website: http://www.opencbs.com
// Contact: contact@opencbs.com

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using OpenCBS.CoreDomain;
using OpenCBS.CoreDomain.Contracts.Loans;
using OpenCBS.CoreDomain.Contracts.Loans.Installments;
using OpenCBS.CoreDomain.Events.Loan;
using OpenCBS.Shared;

namespace OpenCBS.Manager
{
	/// <summary>
	/// Description r�sum�e de InstallmentManagement.
	/// </summary>
	public class InstallmentManager : Manager
	{
	   
        public InstallmentManager(User pUser) : base(pUser)
        {
            
        }

		public InstallmentManager(string pTestDb) : base(pTestDb)
		{
		    
		}

        public void AddInstallments(List<Installment> pInstallments, int pLoanId)
        {
            using (SqlConnection connection = GetConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                AddInstallments(pInstallments, pLoanId, 0, transaction);
                transaction.Commit();
            }            
        }

        public void AddInstallments(List<Installment> pInstallments, int pLoanId, int eventId, SqlTransaction pSqlTransac)
        {
            const string q = @"INSERT INTO InstallmentsTemp(
                                        contract_id, 
                                        event_id, 
                                        number, 
                                        expected_date,
                                        capital_repayment, 
                                        interest_repayment, 
                                        paid_interest, 
                                        paid_capital, 
                                        paid_fees, 
                                        fees_unpaid,
                                        paid_date, 
                                        comment, 
                                        pending,
                                        start_date,
                                        olb) 
                                    VALUES (
                                            @contractId,
                                            @eventId,
                                            @number, 
                                            @expectedDate,
                                            @capitalRepayment, 
                                            @interestsRepayment,
                                            @paidInterests, 
                                            @paidCapital, 
                                            @paidFees, 
                                            @feesUnpaid, 
                                            @paidDate, 
                                            @comment,
                                            @pending,
                                            @startDate,
                                            @olb)";

            using(var c = new OpenCbsCommand(q, pSqlTransac.Connection, pSqlTransac))
            {
                foreach(var installment in pInstallments)
                {
                    SetInstallment(installment,pLoanId,eventId, c);
                    c.ExecuteNonQuery();
                    c.ResetParams();
                }
            }
        }

        public void DeleteInstallments(int pLoanId)
        {
            using (SqlConnection conn = GetConnection())
            using (SqlTransaction t = conn.BeginTransaction())
            {
                DeleteInstallments(pLoanId, t);
                t.Commit();
            }
        }

        public void DeleteInstallments(int pLoanId, SqlTransaction pSqlTransac)
        {
            const string q = @"DELETE FROM InstallmentsTemp WHERE contract_id = @contractId and event_id = 0";

            using (var c = new OpenCbsCommand(q, pSqlTransac.Connection, pSqlTransac))
            {
                c.AddParam("@contractId", pLoanId);
                c.ExecuteNonQuery();
            }
        }

        public List<Installment> SelectInstallments(int pLoanId)
        {
            using (SqlConnection connection = GetConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                List<Installment> list = SelectInstallments(pLoanId, transaction);
                transaction.Commit();
                return list;
            }
        }

	    public List<Installment> SelectInstallments(int pLoanId, SqlTransaction pSqlTransac)
        {
            const string sqlText = @"SELECT 
                                            expected_date, 
                                            interest, 
                                            principal,
                                            number,
                                            paid_interest, 
                                            paid_principal,
                                            fees_unpaid,
                                            paid_date,
                                            paid_fees,
                                            comment,
                                            pending,
                                            start_date,
                                            olb
                                    FROM InstallmentSnapshot(@date) WHERE contract_id = @id";

            using (var c = new OpenCbsCommand(sqlText, pSqlTransac.Connection, pSqlTransac))
            {
                c.AddParam("@id", pLoanId);
                c.AddParam("@date", TimeProvider.Today);
                var installmentList = new List<Installment>();
                using (var r = c.ExecuteReader())
                {
                    if (r.Empty) return installmentList;
                    while (r.Read())
                    {
                        installmentList.Add(GetInstallment(r));
                    }
                }
                return installmentList;
            }
        }

        public List<Tuple<int, int, Installment>> SelectInstallments()
        {
            const string q = @"SELECT 
                                            contract_id,
                                            event_id,
                                            expected_date,
                                            interest_repayment,
                                            capital_repayment,
                                            number,
                                            paid_interest, 
                                            paid_capital,
                                            fees_unpaid,
                                            paid_date,
                                            paid_fees,
                                            comment, 
                                            pending,
                                            start_date,
                                            olb
                                  FROM InstallmentSnapshot(@date)
                                  WHERE paid_capital = 0 
                                    AND paid_interest = 0"; 
            //select only those Installments that have not had any repayments
            using (var conn = GetConnection())
            using (var c = new OpenCbsCommand(q, conn))
            {
                c.AddParam("@date", TimeProvider.Today);
                using (var r = c.ExecuteReader())
                {
                    if (r == null || r.Empty) return new List<Tuple<int, int, Installment>>();

                    var installmentList = new List<Tuple<int, int, Installment>>();
                    while (r.Read())
                    {
                        var result = new Tuple<int, int, Installment>(
                                        r.GetInt("contract_id"), r.GetInt("event_id"), GetInstallment(r));
                        installmentList.Add(result);
                    }
                    return installmentList;
                }
            }
        }

        public List<Installment> GetArchivedInstallments(Event e, SqlTransaction t)
        {
            const string query = @"SELECT number, 
                                          expected_date, 
                                          principal, 
                                          interest, 
                                          paid_interest, 
                                          paid_principal, 
                                          paid_fees, 
                                          fees_unpaid, 
                                          paid_date, 
                                          comment, 
                                          pending,
                                          start_date,
                                          olb
                                    FROM InstallmentSnapshot(@date)";
            using (var c = new OpenCbsCommand(query, t.Connection, t))
            {
                c.AddParam("@date", e.Date.AddSeconds(-1));
                var retval = new List<Installment>();
                using (OpenCbsReader r = c.ExecuteReader())
                {
                    if (null == r || r.Empty) return retval;
                    while (r.Read())
                    {
                        var i = GetInstallmentHistoryFromReader(r);
                        retval.Add(i);
                    }
                }
                
                return retval;
            }
        }

	    private static Installment GetInstallmentHistoryFromReader(OpenCbsReader r)
	    {
	        var i = new Installment
	                    {
	                        Number = r.GetInt("number"),
	                        ExpectedDate = r.GetDateTime("expected_date"),
                            StartDate = r.GetDateTime("start_date"),
	                        CapitalRepayment = r.GetMoney("principal"),
	                        InterestsRepayment = r.GetMoney("interest"),
	                        PaidInterests = r.GetMoney("paid_interest"),
	                        PaidCapital = r.GetMoney("paid_principal"),
	                        PaidFees = r.GetMoney("paid_fees"),
	                        FeesUnpaid = r.GetMoney("fees_unpaid"),
	                        PaidDate = r.GetNullDateTime("paid_date"),
	                        Comment = r.GetString("comment"),
                            OLB = r.GetMoney("olb"),
	                        IsPending = r.GetBool("pending")
	                    };
	        return i;
	    }

	    private static void SetInstallment(Installment pInstallment, int loanId, int eventId, OpenCbsCommand c)
	    {
            //primary key = loanId + number
            c.AddParam("@contractId", loanId);
            c.AddParam("@number", pInstallment.Number);

            c.AddParam("@expectedDate", pInstallment.ExpectedDate);
            c.AddParam("@interestsRepayment", pInstallment.InterestsRepayment.Value);
            c.AddParam("@capitalRepayment", pInstallment.CapitalRepayment.Value);
            c.AddParam("@paidInterests", pInstallment.PaidInterests.Value);
            c.AddParam("@paidCapital", pInstallment.PaidCapital.Value);
            c.AddParam("@paidDate", pInstallment.PaidDate);
            c.AddParam("@feesUnpaid", pInstallment.FeesUnpaid.Value);
            c.AddParam("@paidFees", pInstallment.PaidFees.Value);
            c.AddParam("@comment", pInstallment.Comment);
            c.AddParam("@pending", pInstallment.IsPending);
            c.AddParam("@startDate", pInstallment.StartDate);
            c.AddParam("@olb", pInstallment.OLB);
            c.AddParam("@eventId", eventId);
	    }

        private static Installment GetInstallment(OpenCbsReader r)
        {
            var installment = new Installment
            {
                Number = r.GetInt("number"),
                ExpectedDate = r.GetDateTime("expected_date"),
                InterestsRepayment = r.GetMoney("interest"),
                CapitalRepayment = r.GetMoney("principal"),
                PaidDate = r.GetNullDateTime("paid_date"),
                PaidCapital = r.GetMoney("paid_principal"),
                FeesUnpaid = r.GetMoney("fees_unpaid"),
                PaidInterests = r.GetMoney("paid_interest"),
                PaidFees = r.GetMoney("paid_fees"),
                Comment = r.GetString("comment"),
                IsPending = r.GetBool("pending"),
                StartDate = r.GetDateTime("start_date"),
                OLB = r.GetMoney("olb")
            };
            return installment;
        }

        public void UpdateInstallment(DateTime date, int id, int eventId, int number)
        {
            // Update installement in database
            const string q = @"UPDATE InstallmentsTemp 
                               SET expected_date = @expectedDate
                               WHERE contract_id = @contractId 
                                 AND number = @number
                                 AND event_id = @eventId";

            using (var conn = GetConnection())
            using (var c = new OpenCbsCommand(q, conn))
            {
                //primary key = contractId + number
                c.AddParam("@contractId", id);
                c.AddParam("@number", number);
                c.AddParam("@expectedDate", date);
                c.AddParam("@eventId", eventId);

                c.ExecuteNonQuery();

                c.ResetParams();
                c.CommandText = @"UPDATE dbo.InstallmentsTemp
                                  SET start_date = @start_date
                                  WHERE contract_id = @contractId 
                                    AND number = @number
                                    AND eventId = @eventId";
                c.AddParam("@contractId", id);
                c.AddParam("@number", number + 1);
                c.AddParam("@start_date", date);
                c.AddParam("@eventId", eventId);
                c.ExecuteNonQuery();
            }
        }

        public void UpdateInstallment(int pContractId, int? pEventId, IInstallment installment, bool pRescheduling)
        {
            using (var connection = GetConnection())
            using (var transaction = connection.BeginTransaction())
            {
                UpdateInstallment(installment, pContractId, pEventId, transaction, pRescheduling);
                transaction.Commit();
            }
        }

        /// <summary>
		/// this method allows us to update an installment
		/// </summary>
        /// <param name="pInstallment">the installment modified</param>
        /// <param name="pContractId"></param>
        /// <param name="pEventId">Event linked to this installment update</param>
        /// <param name="pSqlTransac"></param>
        /// <param name="pRescheduling">Is it a rescheduled installment</param>
		public void UpdateInstallment(IInstallment pInstallment,int pContractId, int? pEventId,SqlTransaction pSqlTransac, bool pRescheduling)
		{
            // Update installement in database
			const string q = @"UPDATE InstallmentsTemp 
                                    SET expected_date = @expectedDate, 
                                        interest_repayment = @interestRepayment, 
				                        capital_repayment = @capitalRepayment, 
                                        contract_id = @contractId, 
                                        number = @number, 
                                        paid_interest = @paidInterest, 
				                        paid_capital = @paidCapital,
                                        fees_unpaid = @feesUnpaid, 
                                        paid_date = @paidDate,
                                        paid_fees = @paidFees,
                                        comment = @comment,
                                        pending = @pending,
                                        start_date = @start_date,
                                        olb = @olb
                                     WHERE contract_id = @contractId
                                        AND number = @number
                                        AND event_id = @eventId";

            using (var c = new OpenCbsCommand(q, pSqlTransac.Connection, pSqlTransac))
            {
                //primary key = contractId + number
                c.AddParam("@contractId", pContractId);
                c.AddParam("@number", pInstallment.Number);
                c.AddParam("@expectedDate", pInstallment.ExpectedDate);
                c.AddParam("@interestRepayment", pInstallment.InterestsRepayment.Value);
                c.AddParam("@capitalRepayment", pInstallment.CapitalRepayment.Value);
                c.AddParam("@paidInterest", pInstallment.PaidInterests.Value);
                c.AddParam("@paidCapital", pInstallment.PaidCapital.Value);
                c.AddParam("@paidDate", pInstallment.PaidDate);
                c.AddParam("@paidFees", pInstallment.PaidFees.Value);
                c.AddParam("@comment", pInstallment.Comment);
                c.AddParam("@pending", pInstallment.IsPending);
                c.AddParam("@start_date", pInstallment.StartDate);
                c.AddParam("@olb", pInstallment.OLB);
                c.AddParam("@eventId", pEventId);

                if (pInstallment is Installment)
                {
                    var installment = (Installment) pInstallment;
                    c.AddParam("@feesUnpaid", installment.FeesUnpaid);
                }
                else
                {
                    c.AddParam("@feesUnpaid", 0);
                }

                c.ExecuteNonQuery();
            }
        }

        public void UpdateInstallment(DateTime date, int id, int number)
        {
            // Update installement in database
            const string q = @"UPDATE Installments 
                               SET expected_date = @expectedDate
                               WHERE contract_id = @contractId 
                                 AND number = @number";

            using (var conn = GetConnection())
            using (var c = new OpenCbsCommand(q, conn))
            {
                //primary key = contractId + number
                c.AddParam("@contractId", id);
                c.AddParam("@number", number);
                c.AddParam("@expectedDate", date);

                c.ExecuteNonQuery();

                c.ResetParams();
                c.CommandText = @"UPDATE dbo.Installments
                                  SET start_date = @start_date
                                  WHERE contract_id = @contractId 
                                    AND number = @number";
                c.AddParam("@contractId", id);
                c.AddParam("@number", number + 1);
                c.AddParam("@start_date", date);
                c.ExecuteNonQuery();
            }
        }

        public void UpdateInstallmentComment(string comment, int contractId, int number, int eventId)
        {
            const string q = @"UPDATE InstallmentsTemp
                               SET comment = @comment
                               WHERE contract_id = @contractId 
                                 AND number = @number
                                 AND event_id = @eventId";
            using (var conn = GetConnection())
            using (var c = new OpenCbsCommand(q, conn))
            {
                //primary key = contractId + number
                c.AddParam("@contractId", contractId);
                c.AddParam("@number", number);
                c.AddParam("@comment", comment);
                c.AddParam("@eventId", eventId);

                c.ExecuteNonQuery();
            }
        } 

        public void ArchiveInstallmentList(Loan loan, Event e, SqlTransaction t)
        {
            const string queryUpdate = @"update InstallmentsTemp
                                         set delete_date = @date
                                         where contract_id = @contractId
                                         and event_id = @eventId";

            using (var c = new OpenCbsCommand(queryUpdate, t.Connection, t))
            {
                c.AddParam("@contractId", loan.Id);
                c.AddParam("@eventId", e.Id);
                c.AddParam("@date", TimeProvider.Now);
                c.ExecuteNonQuery();
            }
        }
    }
}
